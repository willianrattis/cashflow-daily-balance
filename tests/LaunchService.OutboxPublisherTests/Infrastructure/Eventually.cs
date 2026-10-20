namespace LaunchService.OutboxPublisherTests.Infrastructure;

/// <summary>
/// Polls until <paramref name="condition"/> is true or <paramref name="timeout"/> elapses. The
/// Outbox Publisher retries on its own background loop (1s poll + backoff), so tests must observe
/// eventual state rather than assert immediately after an action.
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
