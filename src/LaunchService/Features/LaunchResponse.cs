namespace LaunchService.Features;

/// <summary>Response shape shared by record/get, per docs/03-contratos.md.</summary>
public sealed record LaunchResponse(
    Guid LaunchId,
    long AmountInCents,
    string Nature,
    DateOnly BusinessDate,
    DateTime RecordedAtUtc,
    string Status,
    string CorrelationId)
{
    public static LaunchResponse From(Launch launch) => new(
        LaunchId: launch.LaunchId,
        AmountInCents: launch.AmountInCents,
        Nature: launch.Nature.ToString(),
        BusinessDate: launch.BusinessDate,
        RecordedAtUtc: launch.RecordedAtUtc,
        Status: "Recorded",
        CorrelationId: launch.CorrelationId);
}
