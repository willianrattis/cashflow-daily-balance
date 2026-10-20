namespace ConsolidationService.Infrastructure.Clock;

/// <summary>Source of technical (UTC) and business (America/Sao_Paulo) time, injectable for tests.</summary>
public interface IBusinessClock
{
    DateTime UtcNow();
    DateOnly TodayBusinessDate();
}
