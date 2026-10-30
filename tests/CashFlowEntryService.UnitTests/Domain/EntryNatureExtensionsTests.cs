namespace CashFlowEntryService.UnitTests.Domain;

public class EntryNatureExtensionsTests
{
    [Theory]
    [InlineData("Credit", true, EntryNature.Credit)]
    [InlineData("Debit", true, EntryNature.Debit)]
    [InlineData("credit", false, default(EntryNature))]
    [InlineData("Unknown", false, default(EntryNature))]
    [InlineData("", false, default(EntryNature))]
    [InlineData(null, false, default(EntryNature))]
    public void TryParse_only_accepts_exact_Credit_or_Debit(string? input, bool expectedSuccess, EntryNature expectedNature)
    {
        var success = EntryNatureExtensions.TryParse(input, out var nature);

        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedNature, nature);
        }
    }
}
