namespace CashFlowEntryService.IntegrationTests;

/// <summary>
/// Database-level enforcement that must hold even if application code has a bug (INV-001,
/// ADR-008): entries are physically append-only.
/// </summary>
[Collection(CashFlowEntryServiceCollection.Name)]
public class DatabaseIntegrityTests(CashFlowEntryServiceTestFixture fixture)
{
    private async Task<Guid> RecordSampleCashFlowEntryAsync()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.entries.write"));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entries")
        {
            Content = JsonContent.Create(new
            {
                amountInCents = 1000,
                nature = "Credit"
            })
        };
        request.Headers.Add("Idempotency-Key", $"key-{Guid.NewGuid():N}");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("entryId").GetGuid();
    }

    [Fact]
    public async Task Updating_a_entry_row_directly_fails_with_the_append_only_trigger_error()
    {
        var entryId = await RecordSampleCashFlowEntryAsync();

        await using var db = fixture.CreateDbContext();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE cash_flow_entries SET amount_in_cents = 999999 WHERE entry_id = {entryId}"));

        Assert.Contains("append-only", ex.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_a_entry_row_directly_fails_with_the_append_only_trigger_error()
    {
        var entryId = await RecordSampleCashFlowEntryAsync();

        await using var db = fixture.CreateDbContext();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM cash_flow_entries WHERE entry_id = {entryId}"));

        Assert.Contains("append-only", ex.MessageText, StringComparison.OrdinalIgnoreCase);
    }
}
