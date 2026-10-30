namespace CashFlowEntryService.OutboxPublisherTests;

/// <summary>
/// ADR-003: while the broker is down, the claim transaction must not let every claimed
/// row pay for its own doomed connection attempt — one failure should stop the batch immediately
/// instead of holding the `FOR UPDATE` locks open for the duration of 100 separate timeouts.
/// </summary>
[Collection(OutboxPublisherCollection.Name)]
public class OutboxBatchShortCircuitTests(OutboxPublisherTestFixture fixture)
{
    private const int RowCount = 300;

    /// <summary>
    /// The only publish timeout in this project below the production default (5000ms,
    /// appsettings.json), because this is the test that verifies the timeout path: cancelling a
    /// stuck CreateConnectionAsync via CancellationToken does not abort it immediately — measured
    /// empirically, RabbitMQ.Client's internal handshake/continuation cleanup adds a further ~5s
    /// on top of whatever PublishTimeoutMs is configured before the cancellation actually
    /// surfaces to the caller. A smaller value keeps each cycle against the paused broker short
    /// without changing the production default. The other tests use
    /// <see cref="OutboxPublisherTestFixture.LoadSafePublishTimeout"/>.
    /// </summary>
    private static readonly TimeSpan ShortCircuitPublishTimeout = TimeSpan.FromMilliseconds(1500);

    [Fact]
    public async Task When_the_broker_is_down_each_cycle_short_circuits_after_one_row_then_all_publish_once_it_returns()
    {
        await using var host = fixture.StartPublisher(ShortCircuitPublishTimeout);
        await DeclareMainQueueAsync(fixture);

        // Paused before the rows exist, so the running publisher cannot publish any of them in
        // the gap between the insert and the pause.
        var stopwatch = Stopwatch.StartNew();
        await fixture.PauseRabbitMqAsync();

        List<Guid> entryIds;
        List<OutboxMessage> snapshot;
        try
        {
            entryIds = await InsertPendingOutboxRowsAsync(fixture, RowCount);

            // The rows a cycle touched become visible only when it commits. Unlike a hard
            // connection drop, a paused (frozen) broker leaves an already-open channel looking
            // healthy, so the attempt hangs until our own timeout fires rather than failing
            // instantly — about 1.5s on an open channel, about 6.5s when the connection has to be
            // opened (the cleanup overhead above). Waiting for that first commit, rather than a
            // fixed delay, keeps the observation to roughly one cycle however loaded the machine is.
            var firstCycleCommitted = await Eventually.WaitUntilAsync(async () =>
            {
                await using var db = fixture.CreateDbContext();
                return await db.OutboxMessages.AnyAsync(m => entryIds.Contains(m.AggregateId) && m.AttemptCount > 0);
            }, timeout: TimeSpan.FromSeconds(60));
            stopwatch.Stop();

            Assert.True(firstCycleCommitted, "Nenhuma linha foi tocada — o publisher não tentou publicar com o broker fora do ar.");

            await using var snapshotDb = fixture.CreateDbContext();
            snapshot = await snapshotDb.OutboxMessages.Where(m => entryIds.Contains(m.AggregateId)).ToListAsync();
        }
        finally
        {
            // Unlike Stop/Start, Pause/Unpause never tears down the container's network or port
            // mapping, so the broker is reachable again as soon as this returns — no readiness
            // probe needed.
            await fixture.UnpauseRabbitMqAsync();
        }

        var touched = snapshot.Where(m => m.AttemptCount > 0).ToList();
        var untouched = snapshot.Where(m => m.AttemptCount == 0).ToList();

        // 20s covers PauseRabbitMqAsync's own docker call, the insert and one cycle of up to
        // ~6.5s with margin for a loaded CI runner, and still clearly excludes "the short-circuit
        // never kicked in and every row paid for its own doomed attempt": the first cycle would
        // then commit only after its whole claimed batch (BatchSize=100) timed out, minutes
        // later — the actual invariant under test.
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(20),
            $"O primeiro ciclo levou {stopwatch.Elapsed.TotalSeconds:F1}s para gravar; esperava bem menos de 20s.");

        // The invariant here: each cycle only ever touches the ONE row it attempted
        // before short-circuiting — never the whole claimed batch (up to BatchSize=100). The
        // snapshot is taken within one poll of the first commit, but a second cycle can commit
        // in between and a row's ~2-3s backoff can expire and get it retried, so a SMALL number
        // of rows with up to 2 attempts is expected — the invariant under test is "a handful,
        // not dozens" (which is what a broken short-circuit, hammering the whole claimed batch,
        // would look like), not "exactly one attempt".
        Assert.True(touched.Count < 10, $"Esperava poucas linhas tocadas (short-circuit por ciclo), mas {touched.Count} foram tocadas.");
        Assert.All(touched, m => Assert.True(m.AttemptCount is 1 or 2, $"Esperava 1 ou 2 tentativas nesta janela, achei {m.AttemptCount}."));
        Assert.All(touched, m => Assert.Equal(OutboxMessageStatus.Failed, m.Status));
        Assert.All(touched, m => Assert.Contains("connection", m.LastError, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(RowCount - touched.Count, untouched.Count);
        Assert.All(untouched, m => Assert.Equal(0, m.AttemptCount));
        Assert.All(untouched, m => Assert.Equal(OutboxMessageStatus.Pending, m.Status));

        // Generous: under load a confirm after the unpause can still exceed the short timeout,
        // and each such failure pushes that row's next attempt further out (backoff 2s, 4s, 8s...).
        var allPublished = await Eventually.WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            var remaining = await db.OutboxMessages
                .CountAsync(m => entryIds.Contains(m.AggregateId) && m.Status != OutboxMessageStatus.Published);
            return remaining == 0;
        }, timeout: TimeSpan.FromSeconds(120));

        if (!allPublished)
        {
            await using var diagDb = fixture.CreateDbContext();
            var remainingRows = await diagDb.OutboxMessages
                .Where(m => entryIds.Contains(m.AggregateId) && m.Status != OutboxMessageStatus.Published)
                .Select(m => new { m.Status, m.AttemptCount, m.NextAttemptAtUtc, m.LastError })
                .ToListAsync();
            var diagText = string.Join("; ", remainingRows.Take(10).Select(r =>
                $"status={r.Status} attempts={r.AttemptCount} next={r.NextAttemptAtUtc:O} err={r.LastError}"));
            Assert.True(allPublished,
                $"Nem todas as {RowCount} linhas foram publicadas depois que o broker voltou. " +
                $"Restantes: {remainingRows.Count}. Amostra: {diagText}");
        }
    }

    private static async Task<List<Guid>> InsertPendingOutboxRowsAsync(OutboxPublisherTestFixture fixture, int count)
    {
        await using var db = fixture.CreateDbContext();
        var baseTime = DateTime.UtcNow;
        var entryIds = new List<Guid>(count);

        for (var i = 0; i < count; i++)
        {
            // Distinct, strictly increasing timestamps — PublishPendingBatchAsync claims rows
            // `ORDER BY created_at_utc`, so identical timestamps make that order unstable across
            // queries (Postgres ties have no guaranteed tie-break) and let the SAME row keep
            // getting reselected while the rest of the backlog sits untouched, instead of cycling
            // through a new row every cycle like a real backlog (distinct arrival times) would.
            var now = baseTime.AddMilliseconds(i);
            var suffix = $"{i:D6}-{Guid.NewGuid():N}";
            var entry = CashFlowEntry.CreateNew(
                idempotencyKey: $"key-{suffix}",
                amountInCents: 100,
                nature: EntryNature.Credit,
                description: null,
                businessDate: DateOnly.FromDateTime(now),
                recordedAtUtc: now,
                principalId: "shortcircuit-test",
                correlationId: suffix);

            db.Entries.Add(entry);
            db.OutboxMessages.Add(OutboxMessage.FromEntry(entry, now));
            entryIds.Add(entry.EntryId);
        }

        await db.SaveChangesAsync();
        return entryIds;
    }

    private static async Task DeclareMainQueueAsync(OutboxPublisherTestFixture fixture)
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.RabbitMqHostName,
            Port = fixture.RabbitMqPort,
            UserName = OutboxPublisherTestFixture.RabbitMqUserName,
            Password = OutboxPublisherTestFixture.RabbitMqPassword
        };

        await using var connection = await factory.CreateConnectionAsync("outbox-shortcircuit-test-admin");
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(
            exchange: CashFlowTopology.EventsExchange, type: ExchangeType.Topic, durable: true, autoDelete: false);
        await channel.QueueDeclareAsync(queue: CashFlowTopology.ConsolidationQueue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(
            queue: CashFlowTopology.ConsolidationQueue,
            exchange: CashFlowTopology.EventsExchange,
            routingKey: CashFlowTopology.CashFlowEntryRecordedRoutingKey);
    }
}
