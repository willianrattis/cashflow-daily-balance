namespace CashFlowEntryService.Infrastructure.RateLimiting;

public static class PrincipalRateLimitingExtensions
{
    /// <summary>
    /// Reads the RateLimiting section once at build time; the policies below capture these values.
    /// </summary>
    public static RateLimitOptions GetRateLimitOptions(this IConfiguration configuration)
        => configuration.GetSection(RateLimitOptions.SectionName).Get<RateLimitOptions>() ?? new RateLimitOptions();

    // Fixed window per principal (docs/05 §1): partitioned by the `sub` claim so one caller's
    // burst never throttles another. A request without a valid token is rejected by
    // UseAuthorization (401) before it ever reaches this limiter — containing anonymous floods is
    // the edge's job (gateway/WAF, ADR-012), not this in-app limiter's. Health/metrics endpoints
    // are unaffected: they never call RequireRateLimiting, so this policy metadata never attaches
    // to them.
    public static IServiceCollection AddPrincipalRateLimiting(this IServiceCollection services, RateLimitOptions rateLimitOptions)
    {
        services.AddRateLimiter(options =>
        {
            options.OnRejected = RateLimitRejection.HandleAsync;

            options.AddPolicy("writes", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: PrincipalRateLimitPartition.KeyFor(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromSeconds(1),
                    PermitLimit = rateLimitOptions.WriteRequestsPerSecond,
                    QueueLimit = 0
                }));

            options.AddPolicy("reads", httpContext => RateLimitPartition.GetFixedWindowLimiter(
                partitionKey: PrincipalRateLimitPartition.KeyFor(httpContext),
                factory: _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromSeconds(1),
                    PermitLimit = rateLimitOptions.ReadRequestsPerSecond,
                    QueueLimit = 0
                }));
        });

        return services;
    }
}
