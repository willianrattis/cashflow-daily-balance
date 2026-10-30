namespace CashFlowEntryService.Infrastructure.Composition;

public static class RequestPipelineExtensions
{
    /// <summary>
    /// Correlation → authentication → authorization → rate limiter. Authorization runs before the
    /// limiter, so a request without a valid token gets 401 and never consumes a principal's permits.
    /// </summary>
    public static WebApplication UseRequestPipeline(this WebApplication app)
    {
        app.UseMiddleware<CorrelationIdMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();

        return app;
    }
}
