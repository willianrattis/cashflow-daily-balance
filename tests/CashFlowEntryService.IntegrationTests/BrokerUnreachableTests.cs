namespace CashFlowEntryService.IntegrationTests;

/// <summary>
/// NFR-A end to end, against the fixture's intentionally unreachable broker
/// (<see cref="CashFlowEntryServiceTestFixture"/>): readiness and the write path must never depend on
/// RabbitMQ being up.
/// </summary>
[Collection(CashFlowEntryServiceCollection.Name)]
public class BrokerUnreachableTests(CashFlowEntryServiceTestFixture fixture)
{
    [Fact]
    public async Task Health_ready_returns_200_with_the_broker_unreachable()
    {
        var client = fixture.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Recording_a_entry_returns_201_with_the_broker_unreachable()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.entries.write"));

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entries")
        {
            Content = JsonContent.Create(new { amountInCents = 1500, nature = "Credit" })
        };
        request.Headers.Add("Idempotency-Key", "broker-unreachable-health-check");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
