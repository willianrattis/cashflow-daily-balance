namespace CashFlowEntryService.IntegrationTests;

/// <summary>
/// `ValidateOnStart` (ConfiguredOptionsExtensions) must fail the host at startup for an invalid Outbox/RabbitMq/
/// RateLimiting value, instead of letting it through to misbehave at runtime (e.g.
/// `Outbox:BatchSize=0` would otherwise spin a hot loop with an empty claim every cycle). Each
/// test overrides a single env var as a process-wide value, the same constraint documented on
/// <see cref="CashFlowEntryServiceTestFixture"/>; collection tests run sequentially, so nothing else
/// observes the temporary value.
/// </summary>
[Collection(CashFlowEntryServiceCollection.Name)]
#pragma warning disable CS9113 // unused, but required so xUnit initializes the shared fixture (containers, env vars) before this collection's tests run
public class OptionsValidationTests(CashFlowEntryServiceTestFixture fixture)
#pragma warning restore CS9113
{
    [Theory]
    [InlineData("Outbox__BatchSize", "0")]
    [InlineData("Outbox__PollIntervalMs", "0")]
    [InlineData("Outbox__PublishTimeoutMs", "-1")]
    [InlineData("RabbitMq__Port", "0")]
    [InlineData("RabbitMq__HostName", "")]
    [InlineData("RateLimiting__WriteRequestsPerSecond", "0")]
    [InlineData("RateLimiting__ReadRequestsPerSecond", "-5")]
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
}
