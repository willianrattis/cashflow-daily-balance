namespace ConsolidationService.Infrastructure.Messaging;

/// <summary>Wire shape of the `CashFlowEntryRecorded.v1` event, exactly as documented in docs/03-contratos.md.</summary>
public sealed record CashFlowEntryRecordedEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string CausationId,
    CashFlowEntryRecordedPayload? Payload)
{
    public const string SupportedEventType = "CashFlowEntryRecorded";
    public const int SupportedVersion = 1;
}

public sealed record CashFlowEntryRecordedPayload(
    Guid EntryId,
    long AmountInCents,
    string Nature,
    DateOnly BusinessDate,
    DateTime RecordedAtUtc);
