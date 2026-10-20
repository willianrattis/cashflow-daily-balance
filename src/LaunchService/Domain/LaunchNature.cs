namespace LaunchService.Domain;

public enum LaunchNature
{
    Credit,
    Debit
}

public static class LaunchNatureExtensions
{
    public static bool TryParse(string? value, out LaunchNature nature)
        => Enum.TryParse(value, ignoreCase: false, out nature) && Enum.IsDefined(nature);
}
