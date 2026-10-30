namespace CashFlowEntryService.IntegrationTests;

/// <summary>
/// Cross-cutting input validation that must never reach the database or produce an unhandled
/// `500`/default framework body: oversized fields, malformed/unknown-field JSON, an over-limit
/// request body, and a malformed `X-Correlation-Id` (docs/03-contratos.md).
/// </summary>
[Collection(CashFlowEntryServiceCollection.Name)]
public class InputValidationTests(CashFlowEntryServiceTestFixture fixture)
{
    private HttpClient CreateAuthorizedClient()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.entries.write"));
        return client;
    }

    private static HttpRequestMessage BuildRecordRequest(string idempotencyKey, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entries")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", idempotencyKey);
        return request;
    }

    [Fact]
    public async Task Oversized_description_returns_400_invalid_request()
    {
        var client = CreateAuthorizedClient();

        var response = await client.SendAsync(BuildRecordRequest("key-oversized-3", new
        {
            amountInCents = 1000,
            nature = "Credit",
            description = new string('c', 1001)
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_request", body.GetProperty("code").GetString());
        Assert.Equal("description", body.GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Oversized_idempotency_key_returns_400_invalid_request()
    {
        var client = CreateAuthorizedClient();

        var response = await client.SendAsync(BuildRecordRequest(new string('d', 201), new
        {
            amountInCents = 1000,
            nature = "Credit"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_request", body.GetProperty("code").GetString());
        Assert.Equal("Idempotency-Key", body.GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Malformed_json_body_returns_400_with_the_standard_envelope()
    {
        var client = CreateAuthorizedClient();

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entries")
        {
            Content = new StringContent("{ not-valid-json ", Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Idempotency-Key", "key-malformed-1");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("code", out _));
        Assert.True(body.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Unknown_json_field_returns_400_invalid_request()
    {
        var client = CreateAuthorizedClient();

        var response = await client.SendAsync(BuildRecordRequest("key-unknown-field-1", new
        {
            amountInCents = 1000,
            nature = "Credit",
            businessDate = "2020-01-01"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_request", body.GetProperty("code").GetString());
    }

    // The 64 KB body limit is enforced by Kestrel itself (RequestLimitsExtensions:
    // `ConfigureKestrel(... options.Limits.MaxRequestBodySize ...)`), not by application
    // middleware — TestServer (this fixture's host) never runs Kestrel's connection/stream
    // layer, so it cannot reproduce a genuine 413 here. `scripts/e2e/smoke.sh` step
    // `oversized_body_returns_413` exercises this against the real container instead.

    [Fact]
    public async Task Correlation_id_longer_than_100_chars_returns_400_invalid_correlation_id()
    {
        var client = CreateAuthorizedClient();

        var request = BuildRecordRequest("key-correlation-1", new
        {
            amountInCents = 1000,
            nature = "Credit"
        });
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", new string('f', 101));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_correlation_id", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Correlation_id_with_non_printable_characters_returns_400_invalid_correlation_id()
    {
        var client = CreateAuthorizedClient();

        var request = BuildRecordRequest("key-correlation-2", new
        {
            amountInCents = 1000,
            nature = "Credit"
        });
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "abc\u0007def");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_correlation_id", body.GetProperty("code").GetString());
    }
}
