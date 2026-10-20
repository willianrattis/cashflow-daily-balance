namespace ConsolidationService.Infrastructure.Composition;

public static class RequestLimitsExtensions
{
    // 64 KB default (gateways enforce their own body limit in the target architecture, ADR-012);
    // configurable via Kestrel:Limits:MaxRequestBodySize for environments that need a different cap.
    // ConsolidationService has no write endpoint today, but a future one should not silently inherit
    // Kestrel's much larger default (30 MB) just because this line was missing (LaunchService's
    // RequestLimitsExtensions sets the same limit for the same reason).
    public static WebApplicationBuilder AddRequestLimits(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel((context, options) =>
            options.Limits.MaxRequestBodySize = context.Configuration.GetValue("Kestrel:Limits:MaxRequestBodySize", 64 * 1024L));

        return builder;
    }
}
