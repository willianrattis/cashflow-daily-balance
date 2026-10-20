namespace ConsolidationService.IntegrationTests.Infrastructure;

/// <summary>
/// Thin admin-connection helper so tests can inspect queues directly (e.g. the DLQ) instead of
/// only asserting on the database side. Tests in this project's single collection run
/// sequentially against one shared fixture (xUnit default), so a plain `BasicGetAsync` loop is
/// safe — no other test can be producing/consuming on the same queue concurrently.
/// </summary>
public static class TestBrokerAdmin
{
    public static async Task<BasicGetResult?> BasicGetAsync(
        ConsolidationServiceTestFixture fixture, string queue, bool autoAck, CancellationToken ct = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = fixture.RabbitMqHostName,
            Port = fixture.RabbitMqPort,
            UserName = "cashflow",
            Password = "cashflow-test-only"
        };

        await using var connection = await factory.CreateConnectionAsync("integration-test-broker-admin", ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        return await channel.BasicGetAsync(queue, autoAck, ct);
    }

    public static async Task<BasicGetResult?> WaitForMessageAsync(
        ConsolidationServiceTestFixture fixture, string queue, TimeSpan timeout, bool autoAck = true, CancellationToken ct = default)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            var result = await BasicGetAsync(fixture, queue, autoAck, ct);
            if (result is not null || DateTime.UtcNow >= deadline)
            {
                return result;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), ct);
        }
    }
}
