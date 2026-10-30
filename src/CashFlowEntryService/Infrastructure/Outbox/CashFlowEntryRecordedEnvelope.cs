namespace CashFlowEntryService.Infrastructure.Outbox;

/// <summary>Wire shape of the `CashFlowEntryRecorded.v1` event, exactly as documented in docs/03-contratos.md.</summary>
public sealed record CashFlowEntryRecordedEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string CausationId,
    CashFlowEntryRecordedPayload Payload)
{
    public const string TypeName = "CashFlowEntryRecorded";
    public const int CurrentVersion = 1;

    public static CashFlowEntryRecordedEnvelope FromEntry(CashFlowEntry entry, Guid eventId, string causationId) => new(
        EventId: eventId,
        EventType: TypeName,
        EventVersion: CurrentVersion,
        OccurredAtUtc: entry.RecordedAtUtc,
        CorrelationId: entry.CorrelationId,
        CausationId: causationId,
        Payload: new CashFlowEntryRecordedPayload(
            EntryId: entry.EntryId,
            AmountInCents: entry.AmountInCents,
            Nature: entry.Nature.ToString(),
            BusinessDate: entry.BusinessDate,
            RecordedAtUtc: entry.RecordedAtUtc));
}

public sealed record CashFlowEntryRecordedPayload(
    Guid EntryId,
    long AmountInCents,
    string Nature,
    DateOnly BusinessDate,
    DateTime RecordedAtUtc);
