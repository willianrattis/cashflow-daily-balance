namespace ConsolidationService.IntegrationTests.Infrastructure;

/// <summary>
/// Lets a test make the real projector fail N times (or forever) for a specific LaunchId before
/// it runs the real projection logic — the seam that <c>ILaunchRecordedProjector</c> extraction
/// exists for, used through <see cref="FaultInjectingProjector"/>. Keyed by LaunchId so tests
/// running sequentially in the same collection never interfere with each other's plans.
/// </summary>
public sealed class ProjectorFaultController
{
    private sealed record Plan(int RemainingFailures, Func<Exception> ExceptionFactory);

    private readonly ConcurrentDictionary<Guid, Plan> _plans = new();

    public void FailNTimes(Guid launchId, int times, Func<Exception> exceptionFactory) =>
        _plans[launchId] = new Plan(times, exceptionFactory);

    public void FailAlways(Guid launchId, Func<Exception> exceptionFactory) =>
        _plans[launchId] = new Plan(RemainingFailures: -1, exceptionFactory);

    public bool TryConsumeFault(Guid launchId, out Exception? exception)
    {
        exception = null;
        if (!_plans.TryGetValue(launchId, out var plan) || plan.RemainingFailures == 0)
        {
            return false;
        }

        exception = plan.ExceptionFactory();

        if (plan.RemainingFailures > 0)
        {
            var remaining = plan.RemainingFailures - 1;
            if (remaining == 0)
            {
                _plans.TryRemove(launchId, out _);
            }
            else
            {
                _plans[launchId] = plan with { RemainingFailures = remaining };
            }
        }

        return true;
    }
}
