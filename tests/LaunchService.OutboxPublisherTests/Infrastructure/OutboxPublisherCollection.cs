namespace LaunchService.OutboxPublisherTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class OutboxPublisherCollection : ICollectionFixture<OutboxPublisherTestFixture>
{
    public const string Name = "LaunchService Outbox publisher (broker-enabled)";
}
