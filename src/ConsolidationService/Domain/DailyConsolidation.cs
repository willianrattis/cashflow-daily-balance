namespace ConsolidationService.Domain;

/// <summary>
/// Persisted projection of the daily totals (ADR-005). Never the source of truth —
/// derived additively from `CashFlowEntryRecorded.v1` events, and safely rebuildable from the
/// CashFlowEntryService's immutable ledger.
/// </summary>
public sealed class DailyConsolidation
{
    private DailyConsolidation() { }

    public DateOnly BusinessDate { get; private set; }
    public long CreditsInCents { get; private set; }
    public long DebitsInCents { get; private set; }
    public long NetChangeInCents { get; private set; }
    public DateTime LastEventOccurredAtUtc { get; private set; }
    public long ProjectionVersion { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
}
