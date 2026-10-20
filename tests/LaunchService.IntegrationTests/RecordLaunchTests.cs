namespace LaunchService.IntegrationTests;

[Collection(LaunchServiceCollection.Name)]
public class RecordLaunchTests(LaunchServiceTestFixture fixture)
{
    private HttpClient CreateClient() => fixture.CreateClient();

    private static HttpRequestMessage BuildRequest(string idempotencyKey, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/launches")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    [Fact]
    public async Task Registering_a_launch_returns_201_and_persists_outbox_without_publishing()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.write"));

        var response = await client.SendAsync(BuildRequest("key-1", new
        {
            amountInCents = 1000,
            nature = "Credit"
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var launchId = body.GetProperty("launchId").GetGuid();

        await using var db = fixture.CreateDbContext();
        var outbox = await db.OutboxMessages.SingleAsync(o => o.AggregateId == launchId);
        Assert.NotEqual(OutboxMessageStatus.Published, outbox.Status);
    }

    [Fact]
    public async Task Retry_with_same_key_and_same_payload_replays_without_a_second_movement()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.write"));

        object payload = new { amountInCents = 2500, nature = "Credit" };

        var first = await client.SendAsync(BuildRequest("key-2", payload));
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var firstLaunchId = firstBody.GetProperty("launchId").GetGuid();

        var second = await client.SendAsync(BuildRequest("key-2", payload));

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotency-Replayed").Single());
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(firstLaunchId, secondBody.GetProperty("launchId").GetGuid());

        await using var db = fixture.CreateDbContext();
        var count = await db.Launches.CountAsync(l => l.IdempotencyKey == "key-2");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Reusing_the_same_key_with_a_different_payload_returns_409()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.write"));

        await client.SendAsync(BuildRequest("key-3", new
        {
            amountInCents = 1000,
            nature = "Credit"
        }));

        var conflicting = await client.SendAsync(BuildRequest("key-3", new
        {
            amountInCents = 9999,
            nature = "Debit"
        }));

        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        var body = await conflicting.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("idempotency_payload_mismatch", body.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-100)]
    public async Task Non_positive_amount_returns_422(long amountInCents)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.write"));

        var response = await client.SendAsync(BuildRequest("key-6", new
        {
            amountInCents,
            nature = "Credit"
        }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Invalid_nature_returns_422()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.write"));

        var response = await client.SendAsync(BuildRequest("key-7", new
        {
            amountInCents = 1000,
            nature = "Invalid"
        }));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Concurrent_retries_of_the_same_command_produce_exactly_one_launch()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.write"));

        object payload = new { amountInCents = 3000, nature = "Credit" };

        var tasks = Enumerable.Range(0, 8)
            .Select(_ => client.SendAsync(BuildRequest("key-8", payload)))
            .ToArray();
        var responses = await Task.WhenAll(tasks);

        Assert.All(responses, r => Assert.True(r.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK));

        await using var db = fixture.CreateDbContext();
        var count = await db.Launches.CountAsync(l => l.IdempotencyKey == "key-8");
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_400()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.launches.write"));

        var response = await client.PostAsJsonAsync("/api/v1/launches", new
        {
            amountInCents = 1000,
            nature = "Credit"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
