namespace LaunchService.UnitTests.Domain;

public class LaunchTests
{
    private static Launch CreateSample(long amountInCents = 1000, LaunchNature nature = LaunchNature.Credit) =>
        Launch.CreateNew(
            idempotencyKey: "key-1",
            amountInCents: amountInCents,
            nature: nature,
            description: "sale",
            businessDate: new DateOnly(2026, 10, 16),
            recordedAtUtc: new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc),
            principalId: "user-1",
            correlationId: "corr-1");

    [Fact]
    public void CreateNew_rejects_non_positive_amount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSample(amountInCents: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSample(amountInCents: -1));
    }

    [Fact]
    public void CreateNew_assigns_server_controlled_fields()
    {
        var launch = CreateSample();

        Assert.NotEqual(Guid.Empty, launch.LaunchId);
        Assert.Equal(new DateOnly(2026, 10, 16), launch.BusinessDate);
        Assert.False(string.IsNullOrWhiteSpace(launch.RequestFingerprint));
    }
}
