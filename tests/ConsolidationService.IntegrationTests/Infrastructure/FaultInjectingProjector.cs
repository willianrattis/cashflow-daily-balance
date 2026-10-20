namespace ConsolidationService.IntegrationTests.Infrastructure;

/// <summary>
/// Test-only decorator registered in place of the real <c>ILaunchRecordedProjector</c>
/// (<see cref="ConsolidationServiceTestFixture.ConfigureWebHost"/>): throws whatever
/// <see cref="ProjectorFaultController"/> has planned for this event's LaunchId, otherwise
/// delegates straight to the real projector. Lets the classifier tests exercise the
/// retry/DLQ ladder without needing a real Postgres outage.
/// </summary>
public sealed class FaultInjectingProjector(ILaunchRecordedProjector inner, ProjectorFaultController faults) : ILaunchRecordedProjector
{
    public async Task<ProjectionOutcome> ProjectAsync(LaunchRecordedEnvelope envelope, CancellationToken cancellationToken)
    {
        var launchId = envelope.Payload?.LaunchId ?? Guid.Empty;
        if (faults.TryConsumeFault(launchId, out var exception))
        {
            throw exception!;
        }

        return await inner.ProjectAsync(envelope, cancellationToken);
    }
}
