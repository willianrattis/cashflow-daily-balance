namespace ConsolidationService.Infrastructure.Observability;

public static class MetricsExporterExtensions
{
    public static IServiceCollection AddMetricsExporter(this IServiceCollection services)
    {
        services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddMeter(ConsolidationServiceMetrics.MeterName)
                // Default histogram buckets are tuned for sub-second/ms-scale metrics; consumer_lag_seconds
                // measures NFR-005 (targets: p95 <= 5s, p99 <= 30s), so it needs its own boundaries to
                // produce a meaningful histogram_quantile() around those thresholds.
                .AddView(
                    instrumentName: "consumer_lag_seconds",
                    new ExplicitBucketHistogramConfiguration { Boundaries = [0.1, 0.5, 1, 2, 5, 10, 30, 60, 300] })
                .AddPrometheusExporter());

        return services;
    }
}
