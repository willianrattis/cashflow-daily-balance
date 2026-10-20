namespace ConsolidationService.IntegrationTests.Infrastructure;

/// <summary>
/// Polls until <paramref name="condition"/> is true or <paramref name="timeout"/> elapses. Needed
/// because consumption off the broker is asynchronous by design (ADR-002) — tests must observe
/// eventual consistency rather than assert immediately after publishing.
/// </summary>
public static class Eventually
{
    public static async Task<bool> WaitUntilAsync(Func<Task<bool>> condition, TimeSpan? timeout = null, TimeSpan? pollInterval = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        var interval = pollInterval ?? TimeSpan.FromMilliseconds(200);

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(interval);
        }

        return await condition();
    }
}
