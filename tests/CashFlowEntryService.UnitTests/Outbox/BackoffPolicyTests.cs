namespace CashFlowEntryService.UnitTests.Outbox;

public class BackoffPolicyTests
{
    [Fact]
    public void NextDelay_grows_with_attempt_count()
    {
        var first = BackoffPolicy.NextDelay(0);
        var second = BackoffPolicy.NextDelay(1);
        var third = BackoffPolicy.NextDelay(2);

        // Jitter adds up to 1s, so compare against the deterministic floor of each step.
        Assert.True(second.TotalSeconds >= first.TotalSeconds);
        Assert.True(third.TotalSeconds >= second.TotalSeconds);
    }

    [Fact]
    public void NextDelay_is_capped_at_five_minutes_plus_jitter()
    {
        var delay = BackoffPolicy.NextDelay(100);

        Assert.True(delay <= TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void NextDelay_is_always_positive()
    {
        for (var attempt = 0; attempt < 15; attempt++)
        {
            Assert.True(BackoffPolicy.NextDelay(attempt) > TimeSpan.Zero);
        }
    }
}
