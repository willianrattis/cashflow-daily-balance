namespace ConsolidationService.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only decorator registered in place of the real <c>ICashFlowEntryRecordedProjector</c>
/// (<see cref="ConsolidationServiceTestFixture.ConfigureWebHost"/>): throws whatever
/// <see cref="ProjectorFaultController"/> has planned for this event's EntryId, otherwise
/// delegates straight to the real projector. Lets the classifier tests exercise the
/// retry/DLQ ladder without needing a real Postgres outage.
/// </summary>
public sealed class FaultInjectingProjector(ICashFlowEntryRecordedProjector inner, ProjectorFaultController faults) : ICashFlowEntryRecordedProjector
{
    public async Task<ProjectionOutcome> ProjectAsync(CashFlowEntryRecordedEnvelope envelope, CancellationToken cancellationToken)
    {
        var entryId = envelope.Payload?.EntryId ?? Guid.Empty;
        if (faults.TryConsumeFault(entryId, out var exception))
        {
            throw exception!;
        }

        return await inner.ProjectAsync(envelope, cancellationToken);
    }
}
