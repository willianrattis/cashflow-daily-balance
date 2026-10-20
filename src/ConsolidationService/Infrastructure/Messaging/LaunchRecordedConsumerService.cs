namespace ConsolidationService.Infrastructure.Messaging;

/// <summary>
/// Consumes `LaunchRecorded.v1`, deduplicates via Inbox, and updates the daily projection before
/// ACKing (docs/03-contratos.md). ACK only happens after the local transaction commits — a crash
/// or a Consolidation DB outage leaves the message unacknowledged and it is safely redelivered.
///
/// Processing failures are split in two (<see cref="ConsumerFailureClassifier"/>): a transient
/// infrastructure failure (connection drop, timeout, Postgres briefly unreachable) re-parks on
/// the retry ladder forever and never reaches the DLQ, however long the outage lasts (ADR-002); an
/// unexpected failure follows the bounded ladder and only then goes to the DLQ. Republishing to
/// retry/DLQ is itself confirmed by the broker (publisher confirms) before the original delivery
/// is ACKed — if the republish fails, the original is NACKed with `requeue: true` instead, so a
/// broker hiccup during the republish itself can never silently drop the message.
/// </summary>
public sealed class LaunchRecordedConsumerService(
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ConsolidationServiceMetrics metrics,
    IHostEnvironment environment,
    ILogger<LaunchRecordedConsumerService> logger) : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IReadOnlyList<(string QueueName, int DelayMilliseconds)> _retryLevels =
        CashFlowTopology.BuildRetryLevels(options.Value.RetryDelaysMs);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConnectAndConsumeAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Falha ao conectar/consumir do RabbitMQ; nova tentativa em 5s.");
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // shutting down
                }
            }
        }
    }

    private async Task ConnectAndConsumeAsync(CancellationToken stoppingToken)
    {
        var o = options.Value;
        var factory = new ConnectionFactory
        {
            HostName = o.HostName,
            Port = o.Port,
            UserName = o.UserName,
            Password = o.Password,
            VirtualHost = o.VirtualHost,
            AutomaticRecoveryEnabled = true
        };

        await using var connection = await factory.CreateConnectionAsync("consolidationservice-consumer", stoppingToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            stoppingToken);

        await DeclareTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 10, global: false, stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleDeliveryAsync(channel, ea, stoppingToken);

        await channel.BasicConsumeAsync(
            queue: CashFlowTopology.ConsolidationQueue,
            autoAck: false,
            consumerTag: string.Empty,
            noLocal: false,
            exclusive: false,
            arguments: new Dictionary<string, object?>(),
            consumer: consumer,
            cancellationToken: stoppingToken);

        logger.LogInformation("Consumindo a fila {Queue}.", CashFlowTopology.ConsolidationQueue);

        while (!stoppingToken.IsCancellationRequested && channel.IsOpen)
        {
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }

    private async Task DeclareTopologyAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            exchange: CashFlowTopology.EventsExchange, type: ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: CashFlowTopology.ConsolidationQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(
            queue: CashFlowTopology.ConsolidationQueue,
            exchange: CashFlowTopology.EventsExchange,
            routingKey: CashFlowTopology.LaunchRecordedRoutingKey,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            queue: CashFlowTopology.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);

        foreach (var (queueName, delayMs) in _retryLevels)
        {
            var args = new Dictionary<string, object?>
            {
                ["x-message-ttl"] = delayMs,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = CashFlowTopology.ConsolidationQueue
            };
            await channel.QueueDeclareAsync(queue: queueName, durable: true, exclusive: false, autoDelete: false, arguments: args, cancellationToken: ct);
        }
    }

    private async Task HandleDeliveryAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken stoppingToken)
    {
        var retryCount = ReadRetryCount(ea);

        LaunchRecordedEnvelope? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<LaunchRecordedEnvelope>(ea.Body.Span, JsonOptions);
        }
        catch (JsonException)
        {
            envelope = null;
        }

        using var deliveryScope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["service"] = "ConsolidationService",
            ["environment"] = environment.EnvironmentName,
            ["eventId"] = envelope?.EventId,
            ["launchId"] = envelope?.Payload?.LaunchId,
            ["correlationId"] = envelope?.CorrelationId,
            ["retryCount"] = retryCount
        });

        if (!IsValid(envelope, out var reason))
        {
            metrics.ConsumerEventsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "rejected"));
            logger.LogWarning("Evento inválido ({Reason}) enviado à DLQ sem reprocessamento.", reason);
            var republished = await PublishToQueueAsync(channel, CashFlowTopology.DeadLetterQueue, ea, retryCountHeaderValue: null, failureReason: reason, stoppingToken);
            await AckOrNackAsync(channel, ea, republished, stoppingToken);
            return;
        }

        try
        {
            using var scope = scopeFactory.CreateScope();
            var projector = scope.ServiceProvider.GetRequiredService<ILaunchRecordedProjector>();
            var outcome = await projector.ProjectAsync(envelope!, stoppingToken);
            if (outcome == ProjectionOutcome.Duplicate)
            {
                metrics.ConsumerEventsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "duplicate"));
                logger.LogInformation("Evento {EventId} já processado; redelivery ignorada.", envelope!.EventId);
            }
            else
            {
                metrics.ConsumerEventsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "processed"));
                metrics.ConsumerLagSeconds.Record(Math.Max(0, (DateTime.UtcNow - envelope!.OccurredAtUtc).TotalSeconds));
            }

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, stoppingToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var kind = ConsumerFailureClassifier.Classify(ex);
            bool republished;

            if (kind == ConsumerFailureKind.Transient)
            {
                // Never DLQ: climb the ladder, then keep re-parking on the last level until it succeeds.
                var levelIndex = Math.Min(retryCount, _retryLevels.Count - 1);
                var (queueName, _) = _retryLevels[levelIndex];
                metrics.ConsumerEventsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "retry_transient"));
                logger.LogWarning(
                    ex, "Falha transitória processando evento {EventId} (tentativa {RetryCount}); mantendo em retry, nunca DLQ.",
                    envelope!.EventId, retryCount);
                republished = await PublishToQueueAsync(channel, queueName, ea, retryCount + 1, failureReason: null, stoppingToken);
            }
            else if (retryCount < _retryLevels.Count)
            {
                var (queueName, _) = _retryLevels[retryCount];
                metrics.ConsumerEventsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "retry_unexpected"));
                logger.LogWarning(
                    ex, "Falha inesperada processando evento {EventId} (tentativa {RetryCount}); retry limitado.",
                    envelope!.EventId, retryCount);
                republished = await PublishToQueueAsync(channel, queueName, ea, retryCount + 1, failureReason: null, stoppingToken);
            }
            else
            {
                metrics.ConsumerEventsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "dead_lettered"));
                logger.LogWarning(
                    ex, "Falha inesperada processando evento {EventId}; limite de tentativas excedido, enviando à DLQ.", envelope!.EventId);
                republished = await PublishToQueueAsync(channel, CashFlowTopology.DeadLetterQueue, ea, retryCountHeaderValue: null, $"unexpected_failure: {ex.Message}", stoppingToken);
            }

            await AckOrNackAsync(channel, ea, republished, stoppingToken);
        }
    }

    private static bool IsValid(LaunchRecordedEnvelope? envelope, out string reason)
    {
        if (envelope is null)
        {
            reason = "invalid_json";
            return false;
        }

        if (envelope.EventType != LaunchRecordedEnvelope.SupportedEventType)
        {
            reason = "unsupported_event_type";
            return false;
        }

        if (envelope.EventVersion != LaunchRecordedEnvelope.SupportedVersion)
        {
            reason = "unsupported_event_version";
            return false;
        }

        if (envelope.Payload is null || envelope.Payload.LaunchId == Guid.Empty)
        {
            reason = "missing_payload";
            return false;
        }

        if (envelope.Payload.AmountInCents <= 0)
        {
            reason = "invalid_amount";
            return false;
        }

        if (envelope.Payload.Nature is not ("Credit" or "Debit"))
        {
            reason = "invalid_nature";
            return false;
        }

        // default(DateOnly)/default(DateTime) (0001-01-01, ...) can only mean the field was
        // missing or zeroed in the JSON — never a value LaunchService would actually assign.
        // Catching it here, before the projector runs, keeps a malformed
        // producer from silently landing on the earliest possible business date.
        if (envelope.Payload.BusinessDate == default)
        {
            reason = "invalid_business_date";
            return false;
        }

        if (envelope.Payload.RecordedAtUtc == default)
        {
            reason = "invalid_recorded_at";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    private static int ReadRetryCount(BasicDeliverEventArgs ea)
    {
        if (ea.BasicProperties.Headers is not null
            && ea.BasicProperties.Headers.TryGetValue(CashFlowTopology.RetryCountHeader, out var raw))
        {
            return raw switch
            {
                int i => i,
                long l => (int)l,
                byte[] bytes => int.TryParse(Encoding.UTF8.GetString(bytes), out var parsed) ? parsed : 0,
                _ => 0
            };
        }

        return 0;
    }

    /// <summary>
    /// Publishes the original delivery's body onto <paramref name="targetQueue"/> (retry level or
    /// DLQ) with publisher confirms, returning whether the broker actually confirmed it. The
    /// caller must ACK the original only when this returns <c>true</c>;
    /// on <c>false</c> it must NACK with requeue instead — never ACK a message whose
    /// retry/DLQ copy might not exist. Carries over the original delivery's
    /// <see cref="CashFlowTopology.CorrelationIdHeader"/> (set by LaunchService on first publish)
    /// so it stays on the wire across every retry level and the DLQ, not just in the event body.
    /// </summary>
    private async Task<bool> PublishToQueueAsync(
        IChannel channel, string targetQueue, BasicDeliverEventArgs ea, int? retryCountHeaderValue, string? failureReason, CancellationToken ct)
    {
        var headers = new Dictionary<string, object?>();
        if (retryCountHeaderValue is not null)
        {
            headers[CashFlowTopology.RetryCountHeader] = retryCountHeaderValue.Value;
        }

        if (failureReason is not null)
        {
            headers[CashFlowTopology.FailureReasonHeader] = failureReason;
        }

        if (ea.BasicProperties.Headers is not null
            && ea.BasicProperties.Headers.TryGetValue(CashFlowTopology.CorrelationIdHeader, out var correlationIdHeader))
        {
            headers[CashFlowTopology.CorrelationIdHeader] = correlationIdHeader;
        }

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = ea.BasicProperties.ContentType,
            MessageId = ea.BasicProperties.MessageId,
            Headers = headers
        };

        try
        {
            await channel.BasicPublishAsync(
                exchange: string.Empty,
                routingKey: targetQueue,
                mandatory: true,
                basicProperties: properties,
                body: ea.Body,
                cancellationToken: ct);

            if (failureReason is not null)
            {
                metrics.DlqMessagesTotal.Add(1);
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(
                ex, "Falha ao republicar na fila {Queue} (delivery tag {DeliveryTag}); a mensagem original será NACKed com requeue.",
                targetQueue, ea.DeliveryTag);
            return false;
        }
    }

    private static async Task AckOrNackAsync(IChannel channel, BasicDeliverEventArgs ea, bool republished, CancellationToken ct)
    {
        if (republished)
        {
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, ct);
        }
        else
        {
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, ct);
        }
    }
}
