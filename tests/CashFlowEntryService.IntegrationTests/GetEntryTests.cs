namespace CashFlowEntryService.IntegrationTests;

[Collection(CashFlowEntryServiceCollection.Name)]
public class GetEntryTests(CashFlowEntryServiceTestFixture fixture)
{
    [Fact]
    public async Task Getting_an_existing_entry_returns_its_immutable_record()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.entries.write cashflow.entries.read"));

        var recordRequest = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entries")
        {
            Content = JsonContent.Create(new { amountInCents = 777, nature = "Debit" })
        };
        recordRequest.Headers.Add("Idempotency-Key", "get-key-1");
        var recorded = await client.SendAsync(recordRequest);
        var body = await recorded.Content.ReadFromJsonAsync<JsonElement>();
        var entryId = body.GetProperty("entryId").GetGuid();

        var response = await client.GetAsync($"/api/v1/entries/{entryId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(entryId, fetched.GetProperty("entryId").GetGuid());
        Assert.Equal(777, fetched.GetProperty("amountInCents").GetInt64());
        Assert.Equal("Debit", fetched.GetProperty("nature").GetString());
    }

    [Fact]
    public async Task Getting_an_unknown_entry_returns_404()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.entries.read"));

        var response = await client.GetAsync($"/api/v1/entries/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
