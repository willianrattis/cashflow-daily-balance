namespace ConsolidationService.IntegrationTests.Infrastructure;

/// <summary>
/// Lets a test make the real projector fail N times (or forever) for a specific EntryId before
/// it runs the real projection logic — the seam that <c>ICashFlowEntryRecordedProjector</c> extraction
/// exists for, used through <see cref="FaultInjectingProjector"/>. Keyed by EntryId so tests
/// running sequentially in the same collection never interfere with each other's plans.
/// </summary>
public sealed class ProjectorFaultController
{
    private sealed record Plan(int RemainingFailures, Func<Exception> ExceptionFactory);

    private readonly ConcurrentDictionary<Guid, Plan> _plans = new();

    public void FailNTimes(Guid entryId, int times, Func<Exception> exceptionFactory) =>
        _plans[entryId] = new Plan(times, exceptionFactory);

    public void FailAlways(Guid entryId, Func<Exception> exceptionFactory) =>
        _plans[entryId] = new Plan(RemainingFailures: -1, exceptionFactory);

    public bool TryConsumeFault(Guid entryId, out Exception? exception)
    {
        exception = null;
        if (!_plans.TryGetValue(entryId, out var plan) || plan.RemainingFailures == 0)
        {
            return false;
        }

        exception = plan.ExceptionFactory();

        if (plan.RemainingFailures > 0)
        {
            var remaining = plan.RemainingFailures - 1;
            if (remaining == 0)
            {
                _plans.TryRemove(entryId, out _);
            }
            else
            {
                _plans[entryId] = plan with { RemainingFailures = remaining };
            }
        }

        return true;
    }
}
