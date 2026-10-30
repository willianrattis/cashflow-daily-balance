namespace CashFlowEntryService.Domain;

public enum EntryNature
{
    Credit,
    Debit
}

public static class EntryNatureExtensions
{
    public static bool TryParse(string? value, out EntryNature nature)
        => Enum.TryParse(value, ignoreCase: false, out nature) && Enum.IsDefined(nature);
}
