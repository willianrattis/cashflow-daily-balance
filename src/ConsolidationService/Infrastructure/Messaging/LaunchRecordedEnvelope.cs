namespace ConsolidationService.Infrastructure.Messaging;

/// <summary>Wire shape of the `LaunchRecorded.v1` event, exactly as documented in docs/03-contratos.md.</summary>
public sealed record LaunchRecordedEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string CausationId,
    LaunchRecordedPayload? Payload)
{
    public const string SupportedEventType = "LaunchRecorded";
    public const int SupportedVersion = 1;
}

public sealed record LaunchRecordedPayload(
    Guid LaunchId,
    long AmountInCents,
    string Nature,
    DateOnly BusinessDate,
    DateTime RecordedAtUtc);
