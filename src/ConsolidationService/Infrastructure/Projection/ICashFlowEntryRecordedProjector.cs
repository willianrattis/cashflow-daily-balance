namespace ConsolidationService.Infrastructure.Projection;

/// <summary>
/// Applies a validated `CashFlowEntryRecorded.v1` event to the Inbox and the daily projection in one
/// local transaction (ADR-004). Extracted out of <see cref="CashFlowEntryRecordedConsumerService"/> so
/// tests can inject faults (via DI, e.g. <c>ConfigureTestServices</c>) without touching the real
/// broker consumption loop or the ACK/retry/DLQ logic around it.
/// </summary>
public interface ICashFlowEntryRecordedProjector
{
    Task<ProjectionOutcome> ProjectAsync(CashFlowEntryRecordedEnvelope envelope, CancellationToken cancellationToken);
}
