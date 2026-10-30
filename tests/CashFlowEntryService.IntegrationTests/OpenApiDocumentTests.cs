namespace CashFlowEntryService.IntegrationTests;

/// <summary>
/// The generated OpenAPI document must describe the real error envelope (`ApiError`), not the
/// framework's generic `ProblemDetails` — an evaluator opening Swagger should see the contract
/// the API actually returns (docs/03-contratos.md).
/// </summary>
[Collection(CashFlowEntryServiceCollection.Name)]
public class OpenApiDocumentTests(CashFlowEntryServiceTestFixture fixture)
{
    [Theory]
    [InlineData("409")]
    [InlineData("422")]
    public async Task RecordEntry_error_response_references_the_ApiError_schema(string statusCode)
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var responses = root.GetProperty("paths").GetProperty("/api/v1/entries").GetProperty("post").GetProperty("responses");
        var schemaRef = responses.GetProperty(statusCode)
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();
        Assert.Equal("#/components/schemas/ApiError", schemaRef);

        AssertApiErrorSchemaShape(root);
    }

    private static void AssertApiErrorSchemaShape(JsonElement root)
    {
        var apiError = root.GetProperty("components").GetProperty("schemas").GetProperty("ApiError").GetProperty("properties");
        Assert.True(apiError.TryGetProperty("code", out _));
        Assert.True(apiError.TryGetProperty("message", out _));
        Assert.True(apiError.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task RecordEntry_declares_the_Idempotency_Key_header_as_required_and_Idempotency_Replayed_on_the_200_response()
    {
        var client = fixture.CreateClient();
        var document = await FetchDocumentAsync(client);

        var post = document.GetProperty("paths").GetProperty("/api/v1/entries").GetProperty("post");
        var parameters = post.GetProperty("parameters").EnumerateArray().ToList();

        var idempotencyKey = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "Idempotency-Key");
        Assert.Equal("header", idempotencyKey.GetProperty("in").GetString());
        Assert.True(idempotencyKey.GetProperty("required").GetBoolean());

        var correlationId = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "X-Correlation-Id");
        Assert.Equal("header", correlationId.GetProperty("in").GetString());
        Assert.False(correlationId.TryGetProperty("required", out var required) && required.GetBoolean());

        var okHeaders = post.GetProperty("responses").GetProperty("200").GetProperty("headers");
        Assert.True(okHeaders.TryGetProperty("Idempotency-Replayed", out _));
    }

    [Fact]
    public async Task GetEntry_also_declares_the_optional_X_Correlation_Id_header()
    {
        var client = fixture.CreateClient();
        var document = await FetchDocumentAsync(client);

        var get = document.GetProperty("paths").GetProperty("/api/v1/entries/{entryId}").GetProperty("get");
        var parameters = get.GetProperty("parameters").EnumerateArray().ToList();

        Assert.Single(parameters, p => p.GetProperty("name").GetString() == "X-Correlation-Id");
    }

    [Fact]
    public async Task RecordEntryRequest_schema_does_not_require_description()
    {
        var client = fixture.CreateClient();
        var document = await FetchDocumentAsync(client);

        var schema = document.GetProperty("components").GetProperty("schemas").GetProperty("RecordEntryRequest");
        var required = schema.GetProperty("required").EnumerateArray().Select(r => r.GetString()).ToList();

        Assert.DoesNotContain("description", required);
        Assert.Contains("amountInCents", required);
        Assert.Contains("nature", required);
    }

    private static async Task<JsonElement> FetchDocumentAsync(HttpClient client)
    {
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.Clone();
    }
}
