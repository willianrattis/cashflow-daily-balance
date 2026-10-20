namespace ConsolidationService.Domain;

public readonly record struct ConsolidationTotals(
    long OpeningBalanceInCents,
    long CreditsInCents,
    long DebitsInCents,
    long NetChangeInCents,
    long ClosingBalanceInCents);

/// <summary>One calendar day's totals within a daily breakdown (BR-02, FR-003).</summary>
public readonly record struct DailyBreakdownEntry(
    DateOnly BusinessDate,
    long OpeningBalanceInCents,
    long CreditsInCents,
    long DebitsInCents,
    long NetChangeInCents,
    long ClosingBalanceInCents);

/// <summary>
/// Pure arithmetic for the consolidation query (docs/03-contratos.md): opening balance is the sum
/// of all net changes strictly before the window; the window's own credits/debits/net add up to
/// the closing balance. No database access here, so it is trivially unit-testable.
/// </summary>
public static class ConsolidationCalculator
{
    public static ConsolidationTotals Calculate(long netChangeBeforeWindow, long creditsInWindow, long debitsInWindow)
    {
        var netChangeInWindow = creditsInWindow - debitsInWindow;
        var closingBalance = netChangeBeforeWindow + netChangeInWindow;

        return new ConsolidationTotals(
            OpeningBalanceInCents: netChangeBeforeWindow,
            CreditsInCents: creditsInWindow,
            DebitsInCents: debitsInWindow,
            NetChangeInCents: netChangeInWindow,
            ClosingBalanceInCents: closingBalance);
    }

    /// <summary>
    /// One entry per calendar day in <paramref name="fromDate"/>..<paramref name="toDate"/>
    /// (inclusive), including gap days with no movement (zeros). Running balance: day N's
    /// opening is day N-1's closing; the first day's opening is the interval's own opening
    /// (BR-02). A day can close negative when debits exceed what came before (INV-007) — this
    /// function never clamps, it only adds.
    /// </summary>
    public static IReadOnlyList<DailyBreakdownEntry> CalculateDailyBreakdown(
        DateOnly fromDate,
        DateOnly toDate,
        long openingBalanceInCents,
        IReadOnlyDictionary<DateOnly, (long CreditsInCents, long DebitsInCents)> perDayTotals)
    {
        var days = new List<DailyBreakdownEntry>(toDate.DayNumber - fromDate.DayNumber + 1);
        var runningOpening = openingBalanceInCents;

        for (var date = fromDate; date <= toDate; date = date.AddDays(1))
        {
            var (credits, debits) = perDayTotals.TryGetValue(date, out var totalsForDay) ? totalsForDay : (0L, 0L);
            var dayTotals = Calculate(runningOpening, credits, debits);

            days.Add(new DailyBreakdownEntry(
                BusinessDate: date,
                OpeningBalanceInCents: dayTotals.OpeningBalanceInCents,
                CreditsInCents: dayTotals.CreditsInCents,
                DebitsInCents: dayTotals.DebitsInCents,
                NetChangeInCents: dayTotals.NetChangeInCents,
                ClosingBalanceInCents: dayTotals.ClosingBalanceInCents));

            runningOpening = dayTotals.ClosingBalanceInCents;
        }

        return days;
    }
}
