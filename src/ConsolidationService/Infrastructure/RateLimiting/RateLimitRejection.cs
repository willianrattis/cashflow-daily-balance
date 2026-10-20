namespace ConsolidationService.Infrastructure.RateLimiting;

/// <summary>
/// Rate limiter <c>OnRejected</c> callback (docs/05 §1, ADR-010). The limiter answers before the
/// endpoint handler runs, so the handler's own metric/log never see a 429: this callback records
/// it as <c>consolidation_requests_total{outcome="rate_limited"}</c> and writes one Warning log
/// line with the correlation id, then returns the standard error envelope.
/// </summary>
public static class RateLimitRejection
{
    public static async ValueTask HandleAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var httpContext = context.HttpContext;

        httpContext.RequestServices.GetRequiredService<ConsolidationServiceMetrics>().ConsolidationRequestsTotal.Add(
            1, new KeyValuePair<string, object?>("outcome", "rate_limited"));

        httpContext.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(RateLimitRejection).FullName!)
            .LogWarning(
                "Requisição recusada pelo limitador de requisições (429): {Method} {Path}, chamador {PrincipalId}, correlationId {CorrelationId}.",
                httpContext.Request.Method,
                httpContext.Request.Path,
                PrincipalRateLimitPartition.KeyFor(httpContext),
                CorrelationIdMiddleware.GetCorrelationId(httpContext));

        httpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await httpContext.Response.WriteAsJsonAsync(
            new ApiError(
                "rate_limit_exceeded",
                "Muitas requisições; tente novamente em breve.",
                TraceIdAccessor.GetTraceId(httpContext)),
            cancellationToken);
    }
}
