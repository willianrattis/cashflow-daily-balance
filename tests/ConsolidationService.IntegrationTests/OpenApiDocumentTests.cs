namespace ConsolidationService.IntegrationTests;

/// <summary>
/// The generated OpenAPI document must describe the real error envelope (`ApiError`), not the
/// framework's generic `ProblemDetails` — an evaluator opening Swagger should see the contract
/// the API actually returns (docs/03-contratos.md).
/// </summary>
[Collection(ConsolidationServiceCollection.Name)]
public class OpenApiDocumentTests(ConsolidationServiceTestFixture fixture)
{
    [Fact]
    public async Task GetConsolidation_422_response_references_the_ApiError_schema()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;

        var responses = root.GetProperty("paths").GetProperty("/api/v1/consolidation").GetProperty("get").GetProperty("responses");
        var schemaRef = responses.GetProperty("422")
            .GetProperty("content")
            .GetProperty("application/json")
            .GetProperty("schema")
            .GetProperty("$ref")
            .GetString();
        Assert.Equal("#/components/schemas/ApiError", schemaRef);

        var apiError = root.GetProperty("components").GetProperty("schemas").GetProperty("ApiError").GetProperty("properties");
        Assert.True(apiError.TryGetProperty("code", out _));
        Assert.True(apiError.TryGetProperty("message", out _));
        Assert.True(apiError.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task GetConsolidation_declares_fromDate_and_toDate_as_required_and_X_Correlation_Id_as_optional()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var get = document.RootElement.GetProperty("paths").GetProperty("/api/v1/consolidation").GetProperty("get");
        var parameters = get.GetProperty("parameters").EnumerateArray().ToList();

        var fromDate = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "fromDate");
        Assert.Equal("query", fromDate.GetProperty("in").GetString());
        Assert.True(fromDate.GetProperty("required").GetBoolean());

        var toDate = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "toDate");
        Assert.Equal("query", toDate.GetProperty("in").GetString());
        Assert.True(toDate.GetProperty("required").GetBoolean());

        var correlationId = Assert.Single(parameters, p => p.GetProperty("name").GetString() == "X-Correlation-Id");
        Assert.Equal("header", correlationId.GetProperty("in").GetString());
        Assert.False(correlationId.TryGetProperty("required", out var required) && required.GetBoolean());
    }
}
