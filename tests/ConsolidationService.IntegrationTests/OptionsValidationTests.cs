namespace ConsolidationService.IntegrationTests;

/// <summary>
/// `ValidateOnStart` (ConfiguredOptionsExtensions) must fail the host at startup for an invalid RabbitMq/
/// RateLimiting value, instead of letting it through to misbehave at runtime. Each test
/// overrides a single env var as a process-wide value, the same constraint documented on
/// <see cref="ConsolidationServiceTestFixture"/>; collection tests run sequentially, so nothing
/// else observes the temporary value.
/// </summary>
[Collection(ConsolidationServiceCollection.Name)]
#pragma warning disable CS9113 // unused, but required so xUnit initializes the shared fixture (containers, env vars) before this collection's tests run
public class OptionsValidationTests(ConsolidationServiceTestFixture fixture)
#pragma warning restore CS9113
{
    [Theory]
    [InlineData("RabbitMq__Port", "0")]
    [InlineData("RabbitMq__HostName", "")]
    [InlineData("RateLimiting__ReadRequestsPerSecond", "0")]
    public void Startup_fails_for_an_invalid_option_value(string variable, string value)
    {
        Environment.SetEnvironmentVariable(variable, value);
        try
        {
            using var factory = new WebApplicationFactory<Program>();

            var exception = Record.Exception(() => _ = factory.Server);

            Assert.NotNull(exception);
            var actual = exception is System.Reflection.TargetInvocationException { InnerException: { } inner }
                ? inner
                : exception;
            Assert.IsType<OptionsValidationException>(actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    // There is no test here for "zero configured delays" (RabbitMq:RetryDelaysMs.Length > 0):
    // unsetting this project's env var override falls back to appsettings.json's own committed
    // default ([5000, 30000, 120000], a non-empty array), and the .NET configuration binder
    // unions indexed array keys across providers rather than letting a higher-priority source
    // shrink what a lower-priority one already populated — there is no config override that
    // empties the array from outside the process. The `.Length > 0` check is defense-in-depth
    // for a deployment whose own config file omits the section entirely, not independently
    // reachable through this test harness.
    [Fact]
    public void Startup_fails_when_a_retry_ladder_delay_is_not_positive()
    {
        Environment.SetEnvironmentVariable("RabbitMq__RetryDelaysMs__1", "0");
        try
        {
            using var factory = new WebApplicationFactory<Program>();

            var exception = Record.Exception(() => _ = factory.Server);

            Assert.NotNull(exception);
            var actual = exception is System.Reflection.TargetInvocationException { InnerException: { } inner }
                ? inner
                : exception;
            Assert.IsType<OptionsValidationException>(actual);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RabbitMq__RetryDelaysMs__1", "400");
        }
    }
}
