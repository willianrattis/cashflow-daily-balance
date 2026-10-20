namespace LaunchService.OutboxPublisherTests;

/// <summary>
/// An unroutable publish (no queue bound to the routing key — first start, broker
/// recreated, or LaunchService started before ConsolidationService) must never be marked
/// `Published`. These tests guard against `mandatory: false`, which would let RabbitMQ
/// silently accept and drop the message.
/// </summary>
[Collection(OutboxPublisherCollection.Name)]
public class UnroutablePublishTests(OutboxPublisherTestFixture fixture)
{
    [Fact]
    public async Task Publish_without_a_bound_queue_keeps_the_outbox_row_retryable_then_binding_the_queue_delivers_it()
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.RabbitMqHostName,
            Port = fixture.RabbitMqPort,
            UserName = OutboxPublisherTestFixture.RabbitMqUserName,
            Password = OutboxPublisherTestFixture.RabbitMqPassword
        };

        await using var connection = await factory.CreateConnectionAsync("outbox-publisher-test-admin");
        await using var channel = await connection.CreateChannelAsync();

        // Other tests in this collection bind the consumer queue; delete it so this test starts
        // from "no queue bound" whatever order xUnit runs the collection in (the broker
        // recreated, or ConsolidationService not started yet).
        await channel.QueueDeleteAsync(CashFlowTopology.ConsolidationQueue, cancellationToken: CancellationToken.None);

        // A slow broker confirm under load must not be what fails the first attempt: this test
        // checks the unroutable classification, not the timeout.
        await using var host = fixture.StartPublisher(OutboxPublisherTestFixture.LoadSafePublishTimeout);
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.write"));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/launches")
        {
            Content = JsonContent.Create(new
            {
                amountInCents = 1000,
                nature = "Credit"
            })
        };
        request.Headers.Add("Idempotency-Key", "key-unroutable-1");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var launchId = body.GetProperty("launchId").GetGuid();

        // Phase 1: the exchange exists (declared by the publisher itself) but no queue is bound
        // to the routing key — nobody has started a consumer yet. The row must become
        // Failed/retryable, never Published, and attempt_count/last_error must reflect why.
        var becameRetryable = await Eventually.WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            var outbox = await db.OutboxMessages.SingleAsync(o => o.AggregateId == launchId);
            return outbox.Status == OutboxMessageStatus.Failed && outbox.AttemptCount >= 1;
        }, timeout: TimeSpan.FromSeconds(60));

        Assert.True(becameRetryable, "A linha do Outbox deveria ficar Failed/retryable enquanto nenhuma fila está vinculada à routing key.");

        Guid eventId;
        await using (var db = fixture.CreateDbContext())
        {
            var outbox = await db.OutboxMessages.SingleAsync(o => o.AggregateId == launchId);
            eventId = outbox.EventId;
            Assert.NotEqual(OutboxMessageStatus.Published, outbox.Status);
            Assert.True(outbox.AttemptCount >= 1);
            Assert.NotNull(outbox.LastError);
            Assert.Contains("unroutable", outbox.LastError, StringComparison.OrdinalIgnoreCase);
        }

        // Phase 2: declare and bind the queue — simulates ConsolidationService starting late.
        // LaunchService itself never does this (topology ownership stays with the consumer).
        await channel.ExchangeDeclareAsync(
            exchange: CashFlowTopology.EventsExchange, type: ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: CancellationToken.None);
        await channel.QueueDeclareAsync(
            queue: CashFlowTopology.ConsolidationQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: CancellationToken.None);
        await channel.QueueBindAsync(
            queue: CashFlowTopology.ConsolidationQueue,
            exchange: CashFlowTopology.EventsExchange,
            routingKey: CashFlowTopology.LaunchRecordedRoutingKey,
            cancellationToken: CancellationToken.None);

        // Phase 3: once the queue exists, the publisher's own backoff retry must deliver it —
        // nothing besides waiting is needed on the test side.
        var becamePublished = await Eventually.WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            var outbox = await db.OutboxMessages.SingleAsync(o => o.AggregateId == launchId);
            return outbox.Status == OutboxMessageStatus.Published;
        }, timeout: TimeSpan.FromSeconds(60));

        Assert.True(becamePublished, "A linha do Outbox deveria se tornar Published depois que a fila foi declarada e vinculada.");

        // Exactly one copy of this event must have landed in the queue — the failed, unroutable
        // attempts never produced a queued copy, so no duplicate is sitting there. Rows another
        // test left pending can reach the same queue once it is bound, so count this event only.
        var copies = 0;
        while (await channel.BasicGetAsync(CashFlowTopology.ConsolidationQueue, autoAck: true, cancellationToken: CancellationToken.None) is { } delivery)
        {
            if (delivery.BasicProperties.MessageId == eventId.ToString())
            {
                copies++;
            }
        }

        Assert.Equal(1, copies);
    }
}
