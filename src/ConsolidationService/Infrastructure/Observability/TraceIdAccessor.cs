namespace ConsolidationService.Infrastructure.Observability;

/// <summary>
/// Prefers the W3C trace id of the current <see cref="Activity"/> (set by ASP.NET Core's built-in
/// instrumentation for every request) over `HttpContext.TraceIdentifier`, so the `traceId` in logs
/// and in the error envelope already matches what a future tracing backend would show
/// (docs/05-aspectos-transversais.md §3). Falls back to `TraceIdentifier` when no W3C activity is
/// current — e.g. outside a request, or if the id format was never upgraded from the legacy one.
/// </summary>
public static class TraceIdAccessor
{
    public static string GetTraceId(HttpContext httpContext)
    {
        if (Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity)
        {
            var traceId = activity.TraceId.ToHexString();
            if (!string.IsNullOrEmpty(traceId) && traceId != default(ActivityTraceId).ToHexString())
            {
                return traceId;
            }
        }

        return httpContext.TraceIdentifier;
    }
}
