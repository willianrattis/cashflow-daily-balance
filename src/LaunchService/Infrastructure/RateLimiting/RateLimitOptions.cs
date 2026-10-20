namespace LaunchService.Infrastructure.RateLimiting;

/// <summary>
/// Fixed-window, global limits (DoS control, docs/05-aspectos-transversais.md §1). Defaults
/// are well above the documented write/read targets (NFR-011: 50 lançamentos/s; docs/05 §4:
/// 50 req/s de consulta no pico) so the load tests (`scripts/load/run.sh`,
/// docs/evidence/load/) never trip the limiter under normal traffic — they only bound a
/// runaway or malicious client.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int WriteRequestsPerSecond { get; set; } = 100;
    public int ReadRequestsPerSecond { get; set; } = 500;
}
