namespace ConsolidationService.IntegrationTests.Infrastructure;

/// <summary>
/// Publishes directly onto the shared exchange the same way LaunchService's Outbox Publisher
/// would, so these tests exercise the real consumer (<c>LaunchRecordedConsumerService</c>) end to
/// end instead of calling its internals directly.
/// </summary>
public static class TestEventPublisher
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task PublishLaunchRecordedAsync(
        ConsolidationServiceTestFixture fixture,
        Guid launchId,
        long amountInCents,
        string nature,
        DateOnly businessDate,
        DateTime recordedAtUtc,
        Guid? eventId = null,
        string? correlationId = null,
        CancellationToken cancellationToken = default)
    {
        var envelope = new
        {
            eventId = eventId ?? Guid.NewGuid(),
            eventType = "LaunchRecorded",
            eventVersion = 1,
            occurredAtUtc = recordedAtUtc,
            correlationId = correlationId ?? "test-correlation",
            causationId = "test-causation",
            payload = new
            {
                launchId,
                amountInCents,
                nature,
                businessDate,
                recordedAtUtc
            }
        };

        var json = JsonSerializer.Serialize(envelope, JsonOptions);
        return PublishRawAsync(fixture, json, amqpCorrelationIdHeader: correlationId, cancellationToken);
    }

    public static async Task PublishRawAsync(
        ConsolidationServiceTestFixture fixture, string rawJson, string? amqpCorrelationIdHeader = null, CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.RabbitMqHostName,
            Port = fixture.RabbitMqPort,
            UserName = "cashflow",
            Password = "cashflow-test-only"
        };

        await using var connection = await factory.CreateConnectionAsync("integration-test-publisher", cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: CashFlowTopology.EventsExchange, type: ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: cancellationToken);

        var properties = new BasicProperties { Persistent = true, ContentType = "application/json" };
        if (amqpCorrelationIdHeader is not null)
        {
            properties.Headers = new Dictionary<string, object?> { [CashFlowTopology.CorrelationIdHeader] = amqpCorrelationIdHeader };
        }

        var body = Encoding.UTF8.GetBytes(rawJson);

        await channel.BasicPublishAsync(
            exchange: CashFlowTopology.EventsExchange,
            routingKey: CashFlowTopology.LaunchRecordedRoutingKey,
            mandatory: false,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);
    }
}
