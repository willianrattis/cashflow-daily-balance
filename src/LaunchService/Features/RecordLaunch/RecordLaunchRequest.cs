namespace LaunchService.Features.RecordLaunch;

public sealed record RecordLaunchRequest(
    long AmountInCents,
    string? Nature,
    string? Description = null);
