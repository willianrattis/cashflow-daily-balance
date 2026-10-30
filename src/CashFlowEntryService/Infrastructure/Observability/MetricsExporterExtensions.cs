namespace CashFlowEntryService.Infrastructure.Observability;

public static class MetricsExporterExtensions
{
    public static IServiceCollection AddMetricsExporter(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(CashFlowEntryServiceMetrics.MeterName)
                .AddPrometheusExporter());

        return services;
    }
}
