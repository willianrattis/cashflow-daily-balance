namespace LaunchService.IntegrationTests;

/// <summary>
/// Database-level enforcement that must hold even if application code has a bug (INV-001,
/// ADR-008): launches are physically append-only.
/// </summary>
[Collection(LaunchServiceCollection.Name)]
public class DatabaseIntegrityTests(LaunchServiceTestFixture fixture)
{
    private async Task<Guid> RecordSampleLaunchAsync()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.write"));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/launches")
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
        return body.GetProperty("launchId").GetGuid();
    }

    [Fact]
    public async Task Updating_a_launch_row_directly_fails_with_the_append_only_trigger_error()
    {
        var launchId = await RecordSampleLaunchAsync();

        await using var db = fixture.CreateDbContext();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE launches SET amount_in_cents = 999999 WHERE launch_id = {launchId}"));

        Assert.Contains("append-only", ex.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Deleting_a_launch_row_directly_fails_with_the_append_only_trigger_error()
    {
        var launchId = await RecordSampleLaunchAsync();

        await using var db = fixture.CreateDbContext();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM launches WHERE launch_id = {launchId}"));

        Assert.Contains("append-only", ex.MessageText, StringComparison.OrdinalIgnoreCase);
    }
}
