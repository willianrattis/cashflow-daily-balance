namespace LaunchService.UnitTests.Domain;

public class LaunchFingerprintTests
{
    [Fact]
    public void Compute_is_deterministic_for_the_same_inputs()
    {
        var a = LaunchFingerprint.Compute(1000, LaunchNature.Credit, "desc");
        var b = LaunchFingerprint.Compute(1000, LaunchNature.Credit, "desc");

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(2000, LaunchNature.Credit, "desc")]
    [InlineData(1000, LaunchNature.Debit, "desc")]
    [InlineData(1000, LaunchNature.Credit, "different")]
    public void Compute_changes_when_any_relevant_field_changes(long amount, LaunchNature nature, string description)
    {
        var baseline = LaunchFingerprint.Compute(1000, LaunchNature.Credit, "desc");
        var variant = LaunchFingerprint.Compute(amount, nature, description);

        Assert.NotEqual(baseline, variant);
    }

    [Fact]
    public void Compute_treats_null_and_empty_description_the_same()
    {
        var withNull = LaunchFingerprint.Compute(1000, LaunchNature.Credit, null);
        var withEmpty = LaunchFingerprint.Compute(1000, LaunchNature.Credit, string.Empty);

        Assert.Equal(withNull, withEmpty);
    }
}
