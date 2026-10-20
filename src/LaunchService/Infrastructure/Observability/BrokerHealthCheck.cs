namespace LaunchService.Infrastructure.Observability;

/// <summary>
/// Exposed on its own `/health/broker` endpoint, never on `/health/ready` — a broker outage must
/// never make this service report not-ready (NFR-001, ADR-001).
///
/// Caches the result for <see cref="CacheDuration"/> so that frequent polling (e.g. a monitoring
/// dashboard hitting `/health/broker` repeatedly) does not open a fresh RabbitMQ connection on
/// every call — this only has effect because <c>DependencyHealthChecksExtensions</c> registers
/// this type as a singleton before <c>AddHealthChecks().AddCheck&lt;BrokerHealthCheck&gt;(...)</c>:
/// that overload otherwise defaults to a transient registration, which would hand out a
/// brand-new instance (and reset the cache) on every single check.
/// </summary>
public sealed class BrokerHealthCheck(IOptions<RabbitMqOptions> options) : IHealthCheck
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private (HealthCheckResult Result, DateTime CachedAtUtc)? _cache;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (TryGetFreshCache(out var cached))
        {
            return cached;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (TryGetFreshCache(out cached))
            {
                return cached;
            }

            var result = await ProbeAsync(cancellationToken);
            _cache = (result, DateTime.UtcNow);
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }

    private bool TryGetFreshCache(out HealthCheckResult result)
    {
        if (_cache is { } cache && DateTime.UtcNow - cache.CachedAtUtc < CacheDuration)
        {
            result = cache.Result;
            return true;
        }

        result = default;
        return false;
    }

    private async Task<HealthCheckResult> ProbeAsync(CancellationToken cancellationToken)
    {
        var o = options.Value;
        try
        {
            var factory = new ConnectionFactory
            {
                HostName = o.HostName,
                Port = o.Port,
                UserName = o.UserName,
                Password = o.Password,
                VirtualHost = o.VirtualHost,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(3)
            };
            await using var connection = await factory.CreateConnectionAsync("launchservice-health-check", cancellationToken);
            return HealthCheckResult.Healthy();
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Degraded("Não foi possível conectar ao RabbitMQ (não bloqueia o registro de lançamentos).", ex);
        }
    }
}
