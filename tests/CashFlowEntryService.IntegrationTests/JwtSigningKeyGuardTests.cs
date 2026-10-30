namespace CashFlowEntryService.IntegrationTests;

/// <summary>
/// Startup guard (docs/07-arquitetura-alvo.md §8, A-019): outside Development, the service must
/// refuse to start if the configured signing key is the documented development value. Overrides
/// `ASPNETCORE_ENVIRONMENT` as a process environment variable because Program.cs builds its own
/// <c>WebApplicationBuilder</c> inside top-level statements (same constraint documented on
/// <see cref="CashFlowEntryServiceTestFixture"/>); collection tests run sequentially, so nothing else
/// observes the temporary value.
/// </summary>
[Collection(CashFlowEntryServiceCollection.Name)]
#pragma warning disable CS9113 // unused, but required so xUnit initializes the shared fixture (containers, env vars) before this collection's tests run
public class JwtSigningKeyGuardTests(CashFlowEntryServiceTestFixture fixture)
#pragma warning restore CS9113
{
    [Fact]
    public void Startup_fails_outside_development_when_the_signing_key_is_the_documented_dev_value()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        Environment.SetEnvironmentVariable("Jwt__SigningKey", JwtOptions.DocumentedDevSigningKey);
        try
        {
            using var factory = new WebApplicationFactory<Program>();

            var exception = Record.Exception(() => _ = factory.Server);

            Assert.NotNull(exception);
            var actual = exception is System.Reflection.TargetInvocationException { InnerException: { } inner }
                ? inner
                : exception;
            Assert.IsType<InvalidOperationException>(actual);
            Assert.Contains("Development", actual.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
            Environment.SetEnvironmentVariable("Jwt__SigningKey", CashFlowEntryServiceTestFixture.TestSigningKey);
        }
    }
}
