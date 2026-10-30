namespace CashFlowEntryService.Features;

/// <summary>Response shape shared by record/get, per docs/03-contratos.md.</summary>
public sealed record EntryResponse(
    Guid EntryId,
    long AmountInCents,
    string Nature,
    DateOnly BusinessDate,
    DateTime RecordedAtUtc,
    string Status,
    string CorrelationId)
{
    public static EntryResponse From(CashFlowEntry entry) => new(
        EntryId: entry.EntryId,
        AmountInCents: entry.AmountInCents,
        Nature: entry.Nature.ToString(),
        BusinessDate: entry.BusinessDate,
        RecordedAtUtc: entry.RecordedAtUtc,
        Status: "Recorded",
        CorrelationId: entry.CorrelationId);
}
