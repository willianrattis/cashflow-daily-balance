namespace ConsolidationService.Infrastructure.RateLimiting;

/// <summary>
/// Fixed-window, global limit (DoS control, docs/05-aspectos-transversais.md §1).
/// ConsolidationService has no write endpoint, so only the read limit applies here. Default is
/// well above the documented query target (docs/05 §4: 50 req/s de consulta no pico) so the
/// load test (`scripts/load/run.sh consolidation`, docs/evidence/load/) never trips it under
/// normal traffic — it only bounds a runaway or malicious client.
/// </summary>
public sealed class RateLimitOptions
{
    public const string SectionName = "RateLimiting";

    public int ReadRequestsPerSecond { get; set; } = 500;
}
