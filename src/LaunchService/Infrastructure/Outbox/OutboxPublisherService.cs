namespace LaunchService.Infrastructure.Outbox;

/// <summary>
/// Polls `outbox_messages` for pending/retryable rows and publishes them (ADR-003). Claims rows
/// with `SELECT ... FOR UPDATE SKIP LOCKED` inside a local transaction, so running N replicas of
/// this service against the same Launch DB is safe: two replicas can never claim the same row at
/// the same time — each skips whatever the other already has locked and moves on to different
/// rows. Publishing the same event twice is still expected and separately safe (e.g. a crash
/// between a successful publish and committing the row as Published) — the consumer deduplicates
/// by EventId/LaunchId — SKIP LOCKED just means that outcome stays the exception, not something
/// that happens on every cycle under concurrent replicas.
///
/// Drains immediately (no sleep) only when every claimed row in the batch was actually
/// *published* (`Outbox:BatchSize`), since that signals more rows are likely still waiting;
/// sleeps `Outbox:PollIntervalMs` otherwise (ADR-003). A batch where every row claimed still counts
/// toward the drain decision would hammer an already-down broker — the decision is based on
/// published count, not claimed count, precisely to avoid that.
///
/// The claim transaction holds `FOR UPDATE` row locks for as long as this cycle takes, so each
/// message's publish+confirm (including establishing the connection, if needed) is bounded by
/// `Outbox:PublishTimeoutMs` via a linked <see cref="CancellationToken"/>; a timeout is
/// classified the same as any other connection failure (`reason=connection`). The first
/// connection-level failure in a batch short-circuits the rest of it — the remaining claimed rows
/// are left completely untouched (no status change) and simply unlock when the transaction
/// commits, instead of every one of them separately paying for its own doomed connection attempt
/// while a broker outage holds the transaction (and its locks) open for minutes (ADR-003).
/// `unroutable` and `nack` are per-message outcomes (the broker is reachable and responded) and
/// keep trying the rest of the batch as before.
/// </summary>
public sealed class OutboxPublisherService(
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    IOptions<OutboxOptions> options,
    LaunchServiceMetrics metrics,
    IHostEnvironment environment,
    ILogger<OutboxPublisherService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromMilliseconds(options.Value.PollIntervalMs);

        while (!stoppingToken.IsCancellationRequested)
        {
            int published;
            try
            {
                published = await PublishPendingBatchAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha inesperada no ciclo do Outbox Publisher.");
                published = 0;
            }

            if (published >= options.Value.BatchSize)
            {
                continue; // every claimed row published: more rows are likely waiting, drain immediately
            }

            try
            {
                await Task.Delay(pollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutting down
            }
        }
    }

    private async Task<int> PublishPendingBatchAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LaunchDbContext>();
        var batchSize = options.Value.BatchSize;
        var stopwatch = Stopwatch.StartNew();

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var now = DateTime.UtcNow;
        var pending = await db.OutboxMessages
            .FromSqlInterpolated($"""
                SELECT * FROM outbox_messages
                WHERE status = 'Pending' OR (status = 'Failed' AND next_attempt_at_utc <= {now})
                ORDER BY created_at_utc
                LIMIT {batchSize}
                FOR UPDATE SKIP LOCKED
                """)
            .ToListAsync(ct);

        var publishedCount = 0;

        foreach (var message in pending)
        {
            var correlationId = TryGetCorrelationId(message.Payload);
            using var messageScope = logger.BeginScope(new Dictionary<string, object?>
            {
                ["service"] = "LaunchService",
                ["environment"] = environment.EnvironmentName,
                ["eventId"] = message.EventId,
                ["launchId"] = message.AggregateId,
                ["correlationId"] = correlationId
            });

            using var publishTimeoutCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(options.Value.PublishTimeoutMs));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, publishTimeoutCts.Token);

            string reason;
            string lastError;

            try
            {
                await publisher.PublishAsync(message.EventId, CashFlowTopology.LaunchRecordedRoutingKey, message.Payload, correlationId, linkedCts.Token);
                message.MarkPublished(DateTime.UtcNow);
                publishedCount++;
                metrics.OutboxPublishedTotal.Add(1);
                logger.LogInformation(
                    "Evento {EventId} (lançamento {AggregateId}) publicado no broker.", message.EventId, message.AggregateId);
                continue;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw; // real shutdown, not a publish failure to classify/retry
            }
            catch (OperationCanceledException)
            {
                // Only the per-message publishTimeoutCts could have fired at this point.
                reason = "connection";
                lastError = $"connection: publish timed out after {options.Value.PublishTimeoutMs}ms";
            }
            catch (Exception ex)
            {
                (reason, lastError) = ClassifyFailure(ex, CashFlowTopology.LaunchRecordedRoutingKey);
            }

            var delay = BackoffPolicy.NextDelay(message.AttemptCount);
            message.MarkFailed(DateTime.UtcNow.Add(delay), lastError);
            metrics.BrokerPublishFailuresTotal.Add(1, new KeyValuePair<string, object?>("reason", reason));
            logger.LogWarning(
                "Falha ao publicar evento {EventId} ({Reason}); nova tentativa agendada para {NextAttempt}. Erro: {LastError}",
                message.EventId, reason, message.NextAttemptAtUtc, lastError);

            if (reason == "connection")
            {
                // The broker itself is unreachable, not just rejecting this one message — every
                // remaining claimed row would pay for its own doomed connection attempt. Stop here;
                // they stay completely untouched and unlock when the transaction commits below.
                logger.LogWarning(
                    "Interrompendo o lote do Outbox Publisher após falha de conexão/timeout; " +
                    "linhas restantes deste lote ficam intocadas para o próximo ciclo.");
                break;
            }
        }

        if (pending.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        await transaction.CommitAsync(ct);

        metrics.OutboxPublishBatchDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds);

        await ReportBacklogAsync(db, ct);

        return publishedCount;
    }

    /// <summary>
    /// Maps a publish failure to a stable metric label and a human-readable <c>last_error</c>.
    /// <see cref="PublishReturnException"/> (a basic.return — e.g. no queue bound to the routing
    /// key; ADR-003) is checked before its base type <see cref="PublishException"/> (a broker-level
    /// nack unrelated to routing); anything else (connection refused, broker unreachable) is a
    /// transport-level failure.
    /// </summary>
    private static (string Reason, string LastError) ClassifyFailure(Exception ex, string routingKey)
    {
        return ex switch
        {
            PublishReturnException returnEx => (
                "unroutable",
                $"unroutable: no queue bound to routing key '{routingKey}' on exchange '{returnEx.Exchange}' " +
                $"(reply {returnEx.ReplyCode} {returnEx.ReplyText})."),
            PublishException publishEx => (
                "nack",
                $"nack: broker rejected publish sequence {publishEx.PublishSequenceNumber}: {publishEx.Message}"),
            _ => ("connection", $"connection: {ex.Message}")
        };
    }

    /// <summary>Reads just the `correlationId` field for the log scope, without binding the full envelope.</summary>
    private static string? TryGetCorrelationId(string payloadJson)
    {
        try
        {
            using var document = JsonDocument.Parse(payloadJson);
            return document.RootElement.TryGetProperty("correlationId", out var value) ? value.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private async Task ReportBacklogAsync(LaunchDbContext db, CancellationToken ct)
    {
        var pendingCount = await db.OutboxMessages.CountAsync(m => m.Status != OutboxMessageStatus.Published, ct);
        var oldestCreatedAtUtc = await db.OutboxMessages
            .Where(m => m.Status != OutboxMessageStatus.Published)
            .OrderBy(m => m.CreatedAtUtc)
            .Select(m => (DateTime?)m.CreatedAtUtc)
            .FirstOrDefaultAsync(ct);

        var oldestAgeSeconds = oldestCreatedAtUtc is null
            ? 0
            : (DateTime.UtcNow - oldestCreatedAtUtc.Value).TotalSeconds;

        metrics.ReportOutboxBacklog(pendingCount, oldestAgeSeconds);
    }
}
