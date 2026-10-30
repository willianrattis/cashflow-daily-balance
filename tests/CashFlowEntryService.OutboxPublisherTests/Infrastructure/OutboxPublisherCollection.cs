namespace CashFlowEntryService.OutboxPublisherTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class OutboxPublisherCollection : ICollectionFixture<OutboxPublisherTestFixture>
{
    public const string Name = "CashFlowEntryService Outbox publisher (broker-enabled)";
}
