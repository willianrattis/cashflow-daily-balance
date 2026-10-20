namespace ConsolidationService.IntegrationTests;

/// <summary>
/// `GetConsolidationEndpoint` catches `NpgsqlException`/`TimeoutException` around the query and
/// maps it to `503 consolidation_unavailable` (docs/03-contratos.md) instead of a bare 500 — a
/// temporarily unreachable Consolidation DB must not look like a server bug to the caller. This
/// freezes the real Postgres container (no network/port teardown, so the already-bound
/// connection string stays valid once it is unfrozen) to prove it end to end, then confirms the
/// endpoint recovers once the database comes back.
/// </summary>
[Collection(ConsolidationServiceCollection.Name)]
public class DatabaseUnavailableTests(ConsolidationServiceTestFixture fixture)
{
    [Fact]
    public async Task GetConsolidation_returns_503_while_the_database_is_unreachable_and_recovers_afterward()
    {
        var today = UniqueBusinessDate.NextBlockStart();
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        await fixture.PauseDatabaseAsync();
        try
        {
            var response = await client.GetAsync($"/api/v1/consolidation?fromDate={today:yyyy-MM-dd}&toDate={today:yyyy-MM-dd}");

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("consolidation_unavailable", body.GetProperty("code").GetString());
        }
        finally
        {
            await fixture.UnpauseDatabaseAsync();
        }

        var recovered = await Eventually.WaitUntilAsync(async () =>
        {
            var response = await client.GetAsync($"/api/v1/consolidation?fromDate={today:yyyy-MM-dd}&toDate={today:yyyy-MM-dd}");
            return response.StatusCode == HttpStatusCode.OK;
        }, timeout: TimeSpan.FromSeconds(30));

        Assert.True(recovered, "O endpoint deveria voltar a responder 200 assim que o banco do Consolidado voltasse.");
    }
}
