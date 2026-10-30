namespace CashFlowEntryService.Infrastructure.Observability;

/// <summary>
/// Readiness depends only on this service's own database — never on the broker or the
/// ConsolidationService (NFR-001). Broker health is reported separately and is non-blocking.
/// </summary>
public sealed class DatabaseHealthCheck(string connectionString) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Não foi possível conectar ao Cash Flow Entry DB.", ex);
        }
    }
}
