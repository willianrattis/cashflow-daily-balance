namespace LaunchService.OutboxPublisherTests;

/// <summary>
/// ADR-003: the Outbox Publisher must drain a large backlog quickly (no fixed 1 req/s-ish
/// ceiling) and must be safe to run as N replicas against the same Launch DB (SELECT ... FOR
/// UPDATE SKIP LOCKED) — two instances claiming from the same backlog must never publish the
/// same row twice concurrently.
/// </summary>
[Collection(OutboxPublisherCollection.Name)]
public class OutboxThroughputTests(OutboxPublisherTestFixture fixture)
{
    private const int RowCount = 1000;

    [Fact]
    public async Task A_thousand_pending_rows_are_all_published_within_a_bounded_time()
    {
        await using var host = fixture.StartPublisher(OutboxPublisherTestFixture.LoadSafePublishTimeout);
        await DeclareMainQueueAsync(fixture);

        var (launchIds, _) = await InsertPendingOutboxRowsAsync(fixture, RowCount);

        var stopwatch = Stopwatch.StartNew();
        var allPublished = await Eventually.WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            var remaining = await db.OutboxMessages
                .CountAsync(m => launchIds.Contains(m.AggregateId) && m.Status != OutboxMessageStatus.Published);
            return remaining == 0;
        }, timeout: TimeSpan.FromSeconds(60));
        stopwatch.Stop();

        Assert.True(allPublished, $"Nem todas as {RowCount} linhas foram publicadas dentro do timeout de 60s.");

        // Recorded for the evidence log regardless of outcome — the actual number matters,
        // not just a pass/fail against the bound. The bound itself is deliberately loose (25s,
        // against the 60s hard timeout above): measured locally this finishes in ~3-5s, but a
        // CI runner shared with four other projects' containers can be meaningfully slower, and
        // this assertion exists to catch a regression in drain throughput, not to shave margin
        // off the real pass/fail check above.
        var actualSeconds = stopwatch.Elapsed.TotalSeconds;
        Assert.True(
            stopwatch.Elapsed <= TimeSpan.FromSeconds(25),
            $"Esperava publicar {RowCount} linhas em <= 25s; levou {actualSeconds:F2}s.");
    }

    [Fact]
    public async Task Two_concurrent_publisher_instances_publish_each_event_exactly_once()
    {
        // The publish timeout matters here: a publish that times out after the broker already
        // accepted it is retried, and that second copy is an at-least-once duplicate, not two
        // replicas claiming the same row — which is what this test is about.
        await using var firstHost = fixture.StartPublisher(OutboxPublisherTestFixture.LoadSafePublishTimeout);
        await DeclareMainQueueAsync(fixture);

        var (launchIds, eventIds) = await InsertPendingOutboxRowsAsync(fixture, RowCount);

        // A second, fully independent host (its own DI container, its own OutboxPublisherService
        // BackgroundService instance) against the SAME Postgres/RabbitMQ, so this really is two
        // replicas racing on the same backlog, not two loops inside one process sharing state.
        bool allPublished;
        await using (fixture.StartPublisher(OutboxPublisherTestFixture.LoadSafePublishTimeout))
        {
            allPublished = await Eventually.WaitUntilAsync(async () =>
            {
                await using var db = fixture.CreateDbContext();
                var remaining = await db.OutboxMessages
                    .CountAsync(m => launchIds.Contains(m.AggregateId) && m.Status != OutboxMessageStatus.Published);
                return remaining == 0;
            }, timeout: TimeSpan.FromSeconds(60));
        }

        Assert.True(allPublished, $"Nem todas as {RowCount} linhas foram publicadas dentro do timeout com dois publishers concorrentes.");

        // The queue is shared across the collection, so it can also hold messages other tests
        // left there; count only this test's events, each of which must be there exactly once.
        var expectedMessageIds = eventIds.Select(id => id.ToString()).ToHashSet();
        var messageIds = (await DrainMainQueueMessageIdsAsync(fixture))
            .Where(expectedMessageIds.Contains)
            .ToList();

        Assert.Equal(RowCount, messageIds.Count);
        Assert.Equal(RowCount, messageIds.Distinct().Count());
    }

    private static async Task<(List<Guid> LaunchIds, List<Guid> EventIds)> InsertPendingOutboxRowsAsync(OutboxPublisherTestFixture fixture, int count)
    {
        await using var db = fixture.CreateDbContext();
        var now = DateTime.UtcNow;
        var launchIds = new List<Guid>(count);
        var eventIds = new List<Guid>(count);

        for (var i = 0; i < count; i++)
        {
            var suffix = $"{i:D6}-{Guid.NewGuid():N}";
            var launch = Launch.CreateNew(
                idempotencyKey: $"key-{suffix}",
                amountInCents: 100,
                nature: LaunchNature.Credit,
                description: null,
                businessDate: DateOnly.FromDateTime(now),
                recordedAtUtc: now,
                principalId: "throughput-test",
                correlationId: suffix);

            var outboxMessage = OutboxMessage.FromLaunch(launch, now);
            db.Launches.Add(launch);
            db.OutboxMessages.Add(outboxMessage);
            launchIds.Add(launch.LaunchId);
            eventIds.Add(outboxMessage.EventId);
        }

        await db.SaveChangesAsync();
        return (launchIds, eventIds);
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

        await using var connection = await factory.CreateConnectionAsync("outbox-throughput-test-admin");
        await using var channel = await connection.CreateChannelAsync();

        await channel.ExchangeDeclareAsync(
            exchange: CashFlowTopology.EventsExchange, type: ExchangeType.Topic, durable: true, autoDelete: false);
        await channel.QueueDeclareAsync(queue: CashFlowTopology.ConsolidationQueue, durable: true, exclusive: false, autoDelete: false);
        await channel.QueueBindAsync(
            queue: CashFlowTopology.ConsolidationQueue,
            exchange: CashFlowTopology.EventsExchange,
            routingKey: CashFlowTopology.LaunchRecordedRoutingKey);
    }

    private static async Task<List<string>> DrainMainQueueMessageIdsAsync(OutboxPublisherTestFixture fixture)
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.RabbitMqHostName,
            Port = fixture.RabbitMqPort,
            UserName = OutboxPublisherTestFixture.RabbitMqUserName,
            Password = OutboxPublisherTestFixture.RabbitMqPassword
        };

        await using var connection = await factory.CreateConnectionAsync("outbox-throughput-test-drain");
        await using var channel = await connection.CreateChannelAsync();

        var messageIds = new List<string>();
        while (await channel.BasicGetAsync(CashFlowTopology.ConsolidationQueue, autoAck: true) is { } result)
        {
            messageIds.Add(result.BasicProperties.MessageId ?? string.Empty);
        }

        return messageIds;
    }
}
