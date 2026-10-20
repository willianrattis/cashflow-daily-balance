namespace LaunchService.Infrastructure.Outbox;

/// <summary>
/// Transactional Outbox row (ADR-003): written in the same local transaction as the <see cref="Launch"/>
/// it describes, then delivered later by <see cref="OutboxPublisherService"/>. Publishing again after
/// a crash is expected and safe — the consumer deduplicates by EventId/LaunchId.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage() { }

    public Guid OutboxId { get; private set; }
    public Guid AggregateId { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = default!;
    public int EventVersion { get; private set; }
    public string Payload { get; private set; } = default!;
    public DateTime OccurredAtUtc { get; private set; }
    public OutboxMessageStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTime? NextAttemptAtUtc { get; private set; }
    public DateTime? PublishedAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static OutboxMessage FromLaunch(Launch launch, DateTime createdAtUtc)
    {
        var envelope = LaunchRecordedEnvelope.FromLaunch(launch, eventId: Guid.NewGuid(), causationId: launch.IdempotencyKey);
        return new OutboxMessage
        {
            OutboxId = Guid.NewGuid(),
            AggregateId = launch.LaunchId,
            EventId = envelope.EventId,
            EventType = envelope.EventType,
            EventVersion = envelope.EventVersion,
            Payload = JsonSerializer.Serialize(envelope, SerializerOptions),
            OccurredAtUtc = launch.RecordedAtUtc,
            Status = OutboxMessageStatus.Pending,
            AttemptCount = 0,
            NextAttemptAtUtc = createdAtUtc,
            PublishedAtUtc = null,
            LastError = null,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void MarkPublished(DateTime publishedAtUtc)
    {
        Status = OutboxMessageStatus.Published;
        PublishedAtUtc = publishedAtUtc;
        LastError = null;
    }

    public void MarkFailed(DateTime nextAttemptAtUtc, string error)
    {
        Status = OutboxMessageStatus.Failed;
        AttemptCount++;
        NextAttemptAtUtc = nextAttemptAtUtc;
        LastError = error;
    }
}
