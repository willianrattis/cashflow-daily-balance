namespace ConsolidationService.Infrastructure.Observability;

public static class OperationalEndpointsExtensions
{
    public static WebApplication MapOperationalEndpoints(this WebApplication app)
    {
        app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
        app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
        app.MapHealthChecks("/health/broker", new HealthCheckOptions { Predicate = check => check.Tags.Contains("broker") });
        app.MapPrometheusScrapingEndpoint();

        return app;
    }
}
