namespace CashFlowEntryService.Infrastructure.RateLimiting;

/// <summary>
/// Partition key for the rate limiter: per authenticated principal. Every rate-limited endpoint
/// also requires authorization, and <c>UseAuthorization</c> runs before <c>UseRateLimiter</c>
/// (RequestPipelineExtensions), so a request without a valid "sub" claim is rejected with 401 before it ever
/// reaches this partitioner — there is no anonymous case to key by IP here.
/// </summary>
public static class PrincipalRateLimitPartition
{
    public static string KeyFor(HttpContext context) =>
        context.User.FindFirst("sub")?.Value ?? "unknown";
}
