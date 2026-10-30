namespace CashFlowEntryService.Infrastructure.Observability;

public static class StructuredLoggingExtensions
{
    // Structured JSON logs with scopes (docs/05-aspectos-transversais.md §3): correlationId/traceId
    // come from CorrelationIdMiddleware's per-request scope; service/environment are fixed there too.
    public static WebApplicationBuilder AddStructuredLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
        });

        return builder;
    }
}
