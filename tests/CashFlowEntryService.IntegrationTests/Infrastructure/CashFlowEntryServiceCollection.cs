namespace CashFlowEntryService.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class CashFlowEntryServiceCollection : ICollectionFixture<CashFlowEntryServiceTestFixture>
{
    public const string Name = "CashFlowEntryService integration";
}
