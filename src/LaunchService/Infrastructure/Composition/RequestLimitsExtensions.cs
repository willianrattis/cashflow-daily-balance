namespace LaunchService.Infrastructure.Composition;

public static class RequestLimitsExtensions
{
    // 64 KB default (gateways enforce their own body limit in the target architecture, ADR-012);
    // configurable via Kestrel:Limits:MaxRequestBodySize for environments that need a different cap.
    public static WebApplicationBuilder AddRequestLimits(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel((context, options) =>
            options.Limits.MaxRequestBodySize = context.Configuration.GetValue("Kestrel:Limits:MaxRequestBodySize", 64 * 1024L));

        return builder;
    }
}
