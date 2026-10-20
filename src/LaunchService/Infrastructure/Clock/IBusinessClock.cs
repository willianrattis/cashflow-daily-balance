namespace LaunchService.Infrastructure.Clock;

/// <summary>
/// Source of technical (UTC) and business (America/Sao_Paulo) time. Injectable so tests can
/// control "today" instead of depending on the real system clock (INV-003, INV-004).
/// </summary>
public interface IBusinessClock
{
    DateTime UtcNow();
    DateOnly TodayBusinessDate();
}
