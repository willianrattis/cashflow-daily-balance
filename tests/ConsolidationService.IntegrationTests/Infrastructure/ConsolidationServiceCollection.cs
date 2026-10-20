namespace ConsolidationService.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class ConsolidationServiceCollection : ICollectionFixture<ConsolidationServiceTestFixture>
{
    public const string Name = "ConsolidationService integration";
}
