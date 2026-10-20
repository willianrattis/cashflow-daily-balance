namespace ConsolidationService.IntegrationTests;

/// <summary>Negative JWT coverage (MR-06): signature, issuer, audience, expiry and scope.</summary>
[Collection(ConsolidationServiceCollection.Name)]
public class JwtAuthenticationTests(ConsolidationServiceTestFixture fixture)
{
    private const string Path = "/api/v1/consolidation?fromDate=2026-10-01&toDate=2026-10-16";

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_returns_401()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create(
            scope: "cashflow.consolidation.read", expires: DateTime.UtcNow.AddMinutes(-5), notBefore: DateTime.UtcNow.AddMinutes(-10));
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_issuer_returns_401()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create(scope: "cashflow.consolidation.read", issuer: "not-cashflow-dev-issuer");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_audience_returns_401()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create(scope: "cashflow.consolidation.read", audience: "not-cashflow-api");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bad_signature_returns_401()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create(scope: "cashflow.consolidation.read", signingKey: "a-completely-different-signing-key-32-bytes-min");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Missing_scope_returns_403()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create();
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Scope_only_integration_token_with_consolidation_read_returns_200()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create(scope: "cashflow.consolidation.read", subject: "reporting-integration");
        client.DefaultRequestHeaders.Authorization = new("Bearer", token);

        var response = await client.GetAsync(Path);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
