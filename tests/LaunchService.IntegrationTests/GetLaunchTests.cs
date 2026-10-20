namespace LaunchService.IntegrationTests;

[Collection(LaunchServiceCollection.Name)]
public class GetLaunchTests(LaunchServiceTestFixture fixture)
{
    [Fact]
    public async Task Getting_an_existing_launch_returns_its_immutable_record()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.write cashflow.launches.read"));

        var recordRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/launches")
        {
            Content = JsonContent.Create(new { amountInCents = 777, nature = "Debit" })
        };
        recordRequest.Headers.Add("Idempotency-Key", "get-key-1");
        var recorded = await client.SendAsync(recordRequest);
        var body = await recorded.Content.ReadFromJsonAsync<JsonElement>();
        var launchId = body.GetProperty("launchId").GetGuid();

        var response = await client.GetAsync($"/api/v1/launches/{launchId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(launchId, fetched.GetProperty("launchId").GetGuid());
        Assert.Equal(777, fetched.GetProperty("amountInCents").GetInt64());
        Assert.Equal("Debit", fetched.GetProperty("nature").GetString());
    }

    [Fact]
    public async Task Getting_an_unknown_launch_returns_404()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.read"));

        var response = await client.GetAsync($"/api/v1/launches/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
