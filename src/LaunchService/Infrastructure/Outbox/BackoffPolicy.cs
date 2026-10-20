namespace LaunchService.Infrastructure.Outbox;

/// <summary>Exponential backoff with jitter for Outbox publish retries (docs/05-aspectos-transversais.md).</summary>
public static class BackoffPolicy
{
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(5);

    public static TimeSpan NextDelay(int attemptCountBeforeThisFailure)
    {
        var exponent = Math.Min(attemptCountBeforeThisFailure, 10);
        var raw = BaseDelay * Math.Pow(2, exponent);
        var capped = raw > MaxDelay ? MaxDelay : raw;
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 1000));
        return capped + jitter;
    }
}
