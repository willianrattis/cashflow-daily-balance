namespace CashFlowEntryService.IntegrationTests;

/// <summary>
/// The `400 invalid_request` envelope for a malformed or unknown-field body must not depend on
/// the environment (docs/03-contratos.md). Minimal APIs only throw (and so only reach
/// DomainExceptionHandler) when <c>RouteHandlerOptions.ThrowOnBadRequest</c> is on, which the
/// framework enables by default only in Development — so this test builds a second host with
/// `ASPNETCORE_ENVIRONMENT=Production` (process environment variable, same constraint as
/// <see cref="JwtSigningKeyGuardTests"/>; collection tests run sequentially). The shared fixture
/// already migrated the database and set a non-development signing key.
/// </summary>
[Collection(CashFlowEntryServiceCollection.Name)]
#pragma warning disable CS9113 // unused, but required so xUnit initializes the shared fixture (containers, env vars) before this collection's tests run
public class ProductionErrorEnvelopeTests(CashFlowEntryServiceTestFixture fixture)
#pragma warning restore CS9113
{
    public static TheoryData<string> InvalidBodies => new()
    {
        "{ not-valid-json ",
        """{"amountInCents":1000,"nature":"Credit","businessDate":"2020-01-01"}"""
    };

    [Theory]
    [MemberData(nameof(InvalidBodies))]
    public async Task Invalid_body_returns_400_with_the_standard_envelope_in_production(string body)
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Production");
        WebApplicationFactory<Program> productionFactory = new();
        try
        {
            var client = productionFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.entries.write"));
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entries")
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.TryAddWithoutValidation("Idempotency-Key", $"key-production-{Guid.NewGuid():N}");

            var response = await client.SendAsync(request);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            var envelope = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("invalid_request", envelope.GetProperty("code").GetString());
            Assert.True(envelope.TryGetProperty("traceId", out _));
        }
        finally
        {
            productionFactory.Dispose();
            Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        }
    }
}
