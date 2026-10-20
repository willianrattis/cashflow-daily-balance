namespace LaunchService.Infrastructure.Outbox;

/// <summary>Wire shape of the `LaunchRecorded.v1` event, exactly as documented in docs/03-contratos.md.</summary>
public sealed record LaunchRecordedEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTime OccurredAtUtc,
    string CorrelationId,
    string CausationId,
    LaunchRecordedPayload Payload)
{
    public const string TypeName = "LaunchRecorded";
    public const int CurrentVersion = 1;

    public static LaunchRecordedEnvelope FromLaunch(Launch launch, Guid eventId, string causationId) => new(
        EventId: eventId,
        EventType: TypeName,
        EventVersion: CurrentVersion,
        OccurredAtUtc: launch.RecordedAtUtc,
        CorrelationId: launch.CorrelationId,
        CausationId: causationId,
        Payload: new LaunchRecordedPayload(
            LaunchId: launch.LaunchId,
            AmountInCents: launch.AmountInCents,
            Nature: launch.Nature.ToString(),
            BusinessDate: launch.BusinessDate,
            RecordedAtUtc: launch.RecordedAtUtc));
}

public sealed record LaunchRecordedPayload(
    Guid LaunchId,
    long AmountInCents,
    string Nature,
    DateOnly BusinessDate,
    DateTime RecordedAtUtc);
