namespace LaunchService.UnitTests.Domain;

public class LaunchNatureExtensionsTests
{
    [Theory]
    [InlineData("Credit", true, LaunchNature.Credit)]
    [InlineData("Debit", true, LaunchNature.Debit)]
    [InlineData("credit", false, default(LaunchNature))]
    [InlineData("Unknown", false, default(LaunchNature))]
    [InlineData("", false, default(LaunchNature))]
    [InlineData(null, false, default(LaunchNature))]
    public void TryParse_only_accepts_exact_Credit_or_Debit(string? input, bool expectedSuccess, LaunchNature expectedNature)
    {
        var success = LaunchNatureExtensions.TryParse(input, out var nature);

        Assert.Equal(expectedSuccess, success);
        if (expectedSuccess)
        {
            Assert.Equal(expectedNature, nature);
        }
    }
}
