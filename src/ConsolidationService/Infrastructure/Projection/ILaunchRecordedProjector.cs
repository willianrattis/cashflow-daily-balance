namespace ConsolidationService.Infrastructure.Projection;

/// <summary>
/// Applies a validated `LaunchRecorded.v1` event to the Inbox and the daily projection in one
/// local transaction (ADR-004). Extracted out of <see cref="LaunchRecordedConsumerService"/> so
/// tests can inject faults (via DI, e.g. <c>ConfigureTestServices</c>) without touching the real
/// broker consumption loop or the ACK/retry/DLQ logic around it.
/// </summary>
public interface ILaunchRecordedProjector
{
    Task<ProjectionOutcome> ProjectAsync(LaunchRecordedEnvelope envelope, CancellationToken cancellationToken);
}
