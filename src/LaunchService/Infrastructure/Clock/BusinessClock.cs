namespace LaunchService.Infrastructure.Clock;

public sealed class BusinessClock : IBusinessClock
{
    public const string BusinessTimeZoneId = "America/Sao_Paulo";

    private static readonly TimeZoneInfo BusinessTimeZone = ResolveTimeZone();

    public DateTime UtcNow() => DateTime.UtcNow;

    public DateOnly TodayBusinessDate()
    {
        var nowInBusinessZone = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, BusinessTimeZone);
        return DateOnly.FromDateTime(nowInBusinessZone);
    }

    private static TimeZoneInfo ResolveTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(BusinessTimeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            // Windows containers/hosts use a different IANA->Windows ID mapping.
            return TimeZoneInfo.FindSystemTimeZoneById("E. South America Standard Time");
        }
    }
}
