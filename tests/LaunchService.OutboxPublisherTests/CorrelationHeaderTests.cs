namespace LaunchService.OutboxPublisherTests;

/// <summary>
/// docs/05-aspectos-transversais.md §3: the Outbox Publisher must stamp the launch's `correlationId` as the
/// AMQP header `x-correlation-id`, so it is observable on the wire without parsing the JSON body —
/// e.g. for a broker-side trace or a manual inspection of a stuck message.
/// </summary>
[Collection(OutboxPublisherCollection.Name)]
public class CorrelationHeaderTests(OutboxPublisherTestFixture fixture)
{
    [Fact]
    public async Task Published_message_carries_the_launch_correlationId_as_an_amqp_header()
    {
        // Bind the consumer queue upfront so this publish succeeds on the Outbox Publisher's
        // first attempt — this test is about the header, not about the unroutable-retry path
        // (already covered by UnroutablePublishTests).
        var factory = new ConnectionFactory
        {
            HostName = fixture.RabbitMqHostName,
            Port = fixture.RabbitMqPort,
            UserName = OutboxPublisherTestFixture.RabbitMqUserName,
            Password = OutboxPublisherTestFixture.RabbitMqPassword
        };
        await using var adminConnection = await factory.CreateConnectionAsync("outbox-publisher-test-admin-correlation");
        await using var adminChannel = await adminConnection.CreateChannelAsync();
        await adminChannel.ExchangeDeclareAsync(
            exchange: CashFlowTopology.EventsExchange, type: ExchangeType.Topic, durable: true, autoDelete: false,
            cancellationToken: CancellationToken.None);
        await adminChannel.QueueDeclareAsync(
            queue: CashFlowTopology.ConsolidationQueue, durable: true, exclusive: false, autoDelete: false,
            cancellationToken: CancellationToken.None);
        await adminChannel.QueueBindAsync(
            queue: CashFlowTopology.ConsolidationQueue,
            exchange: CashFlowTopology.EventsExchange,
            routingKey: CashFlowTopology.LaunchRecordedRoutingKey,
            cancellationToken: CancellationToken.None);

        await using var host = fixture.StartPublisher(OutboxPublisherTestFixture.LoadSafePublishTimeout);
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.write"));

        var expectedCorrelationId = $"corr-{Guid.NewGuid():N}";
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/launches")
        {
            Content = JsonContent.Create(new
            {
                amountInCents = 1000,
                nature = "Credit"
            })
        };
        request.Headers.Add("Idempotency-Key", "key-correlation-1");
        request.Headers.Add("X-Correlation-Id", expectedCorrelationId);

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var launchId = body.GetProperty("launchId").GetGuid();

        var becamePublished = await Eventually.WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            var outbox = await db.OutboxMessages.SingleAsync(o => o.AggregateId == launchId);
            return outbox.Status == OutboxMessageStatus.Published;
        }, timeout: TimeSpan.FromSeconds(60));
        Assert.True(becamePublished, "A linha do Outbox deveria se tornar Published.");

        // The queue is shared across this whole test collection and other tests in it leave
        // unconsumed messages behind (e.g. OutboxThroughputTests's 1000-row backlog), so this
        // publish is not necessarily the first message sitting there — scan for the delivery
        // whose payload.launchId matches this launch, draining (and discarding) whatever
        // unrelated backlog sits ahead of it.
        BasicGetResult? delivery = null;
        for (var attempt = 0; attempt < 5000; attempt++)
        {
            var candidate = await adminChannel.BasicGetAsync(CashFlowTopology.ConsolidationQueue, autoAck: true, cancellationToken: CancellationToken.None);
            if (candidate is null)
            {
                break;
            }

            using var candidateEnvelope = JsonDocument.Parse(candidate.Body);
            if (candidateEnvelope.RootElement.GetProperty("payload").GetProperty("launchId").GetGuid() == launchId)
            {
                delivery = candidate;
                break;
            }
        }

        Assert.NotNull(delivery);

        var headerValue = delivery!.BasicProperties.Headers![CashFlowTopology.CorrelationIdHeader];
        var actualCorrelationId = headerValue switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string s => s,
            _ => null
        };

        Assert.Equal(expectedCorrelationId, actualCorrelationId);
    }
}
