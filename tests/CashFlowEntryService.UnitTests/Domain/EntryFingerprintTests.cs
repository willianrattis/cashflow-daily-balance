namespace CashFlowEntryService.UnitTests.Domain;

public class EntryFingerprintTests
{
    [Fact]
    public void Compute_is_deterministic_for_the_same_inputs()
    {
        var a = EntryFingerprint.Compute(1000, EntryNature.Credit, "desc");
        var b = EntryFingerprint.Compute(1000, EntryNature.Credit, "desc");

        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData(2000, EntryNature.Credit, "desc")]
    [InlineData(1000, EntryNature.Debit, "desc")]
    [InlineData(1000, EntryNature.Credit, "different")]
    public void Compute_changes_when_any_relevant_field_changes(long amount, EntryNature nature, string description)
    {
        var baseline = EntryFingerprint.Compute(1000, EntryNature.Credit, "desc");
        var variant = EntryFingerprint.Compute(amount, nature, description);

        Assert.NotEqual(baseline, variant);
    }

    [Fact]
    public void Compute_treats_null_and_empty_description_the_same()
    {
        var withNull = EntryFingerprint.Compute(1000, EntryNature.Credit, null);
        var withEmpty = EntryFingerprint.Compute(1000, EntryNature.Credit, string.Empty);

        Assert.Equal(withNull, withEmpty);
    }
}
