namespace LaunchService.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class LaunchServiceCollection : ICollectionFixture<LaunchServiceTestFixture>
{
    public const string Name = "LaunchService integration";
}
