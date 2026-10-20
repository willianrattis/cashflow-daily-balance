namespace LaunchService.IntegrationTests;

/// <summary>Negative JWT coverage (MR-06): signature, issuer, audience, expiry and scope.</summary>
[Collection(LaunchServiceCollection.Name)]
public class JwtAuthenticationTests(LaunchServiceTestFixture fixture)
{
    private static HttpRequestMessage BuildRecordRequest(string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/launches")
        {
            Content = JsonContent.Create(new
            {
                amountInCents = 1000,
                nature = "Credit"
            })
        };
        if (token is not null)
        {
            request.Headers.Authorization = new("Bearer", token);
        }
        request.Headers.Add("Idempotency-Key", $"key-jwt-{Guid.NewGuid():N}");
        return request;
    }

    [Fact]
    public async Task Anonymous_request_returns_401()
    {
        var client = fixture.CreateClient();

        var response = await client.SendAsync(BuildRecordRequest(token: null));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Expired_token_returns_401()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create(
            scope: "cashflow.launches.write", expires: DateTime.UtcNow.AddMinutes(-5), notBefore: DateTime.UtcNow.AddMinutes(-10));

        var response = await client.SendAsync(BuildRecordRequest(token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_issuer_returns_401()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create(scope: "cashflow.launches.write", issuer: "not-cashflow-dev-issuer");

        var response = await client.SendAsync(BuildRecordRequest(token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Wrong_audience_returns_401()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create(scope: "cashflow.launches.write", audience: "not-cashflow-api");

        var response = await client.SendAsync(BuildRecordRequest(token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Bad_signature_returns_401()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create(scope: "cashflow.launches.write", signingKey: "a-completely-different-signing-key-32-bytes-min");

        var response = await client.SendAsync(BuildRecordRequest(token));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Missing_scope_returns_403()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create(scope: "cashflow.launches.read");

        var response = await client.SendAsync(BuildRecordRequest(token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Scope_only_integration_token_with_launches_write_returns_201()
    {
        var client = fixture.CreateClient();
        var token = TestTokenFactory.Create(scope: "cashflow.launches.write", subject: "pos-integration");

        var response = await client.SendAsync(BuildRecordRequest(token));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
