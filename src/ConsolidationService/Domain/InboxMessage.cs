namespace ConsolidationService.Domain;

/// <summary>
/// Records that an event has been received/applied, so a redelivered `CashFlowEntryRecorded.v1` becomes
/// a safe no-op instead of double-counting (ADR-004). Unique on EventId (PK) and again on
/// EntryId: either collision means "already processed".
/// </summary>
public sealed class InboxMessage
{
    private InboxMessage() { }

    public Guid EventId { get; private set; }
    public Guid EntryId { get; private set; }
    public string EventType { get; private set; } = default!;
    public int EventVersion { get; private set; }
    public DateTime ReceivedAtUtc { get; private set; }
    public DateTime? ProcessedAtUtc { get; private set; }
    public InboxMessageStatus Status { get; private set; }

    public static InboxMessage CreateProcessed(
        Guid eventId,
        Guid entryId,
        string eventType,
        int eventVersion,
        DateTime receivedAtUtc,
        DateTime processedAtUtc) => new()
    {
        EventId = eventId,
        EntryId = entryId,
        EventType = eventType,
        EventVersion = eventVersion,
        ReceivedAtUtc = receivedAtUtc,
        ProcessedAtUtc = processedAtUtc,
        Status = InboxMessageStatus.Processed
    };
}
