namespace ConsolidationService.UnitTests.Domain;

public class ConsolidationCalculatorTests
{
    [Fact]
    public void Calculate_computes_closing_as_opening_plus_net_change()
    {
        var totals = ConsolidationCalculator.Calculate(netChangeBeforeWindow: 100_000, creditsInWindow: 50_000, debitsInWindow: 20_000);

        Assert.Equal(100_000, totals.OpeningBalanceInCents);
        Assert.Equal(50_000, totals.CreditsInCents);
        Assert.Equal(20_000, totals.DebitsInCents);
        Assert.Equal(30_000, totals.NetChangeInCents);
        Assert.Equal(130_000, totals.ClosingBalanceInCents);
    }

    [Fact]
    public void Calculate_returns_all_zeros_for_an_empty_period_with_no_prior_history()
    {
        var totals = ConsolidationCalculator.Calculate(netChangeBeforeWindow: 0, creditsInWindow: 0, debitsInWindow: 0);

        Assert.Equal(0, totals.OpeningBalanceInCents);
        Assert.Equal(0, totals.NetChangeInCents);
        Assert.Equal(0, totals.ClosingBalanceInCents);
    }

    [Fact]
    public void Calculate_allows_negative_opening_balance_when_debits_exceed_history()
    {
        var totals = ConsolidationCalculator.Calculate(netChangeBeforeWindow: -5_000, creditsInWindow: 1_000, debitsInWindow: 0);

        Assert.Equal(-5_000, totals.OpeningBalanceInCents);
        Assert.Equal(-4_000, totals.ClosingBalanceInCents);
    }

    [Fact]
    public void CalculateDailyBreakdown_carries_closing_forward_as_the_next_days_opening()
    {
        var day1 = new DateOnly(2026, 10, 10);
        var day2 = new DateOnly(2026, 10, 11);
        var day3 = new DateOnly(2026, 10, 12);
        var perDayTotals = new Dictionary<DateOnly, (long, long)>
        {
            [day1] = (5_000, 0),
            [day3] = (0, 2_000)
            // day2 is a gap — no movement, must still appear with opening == day1's closing.
        };

        var days = ConsolidationCalculator.CalculateDailyBreakdown(day1, day3, openingBalanceInCents: 1_000, perDayTotals);

        Assert.Equal(3, days.Count);

        Assert.Equal(day1, days[0].BusinessDate);
        Assert.Equal(1_000, days[0].OpeningBalanceInCents);
        Assert.Equal(5_000, days[0].CreditsInCents);
        Assert.Equal(6_000, days[0].ClosingBalanceInCents);

        Assert.Equal(day2, days[1].BusinessDate);
        Assert.Equal(6_000, days[1].OpeningBalanceInCents); // day1's closing
        Assert.Equal(0, days[1].CreditsInCents);
        Assert.Equal(0, days[1].DebitsInCents);
        Assert.Equal(6_000, days[1].ClosingBalanceInCents); // no movement: closing == opening

        Assert.Equal(day3, days[2].BusinessDate);
        Assert.Equal(6_000, days[2].OpeningBalanceInCents); // day2's closing
        Assert.Equal(2_000, days[2].DebitsInCents);
        Assert.Equal(4_000, days[2].ClosingBalanceInCents);

        Assert.Equal(days[^1].ClosingBalanceInCents, days[2].ClosingBalanceInCents);
    }

    [Fact]
    public void CalculateDailyBreakdown_allows_a_day_to_close_negative_when_debits_exceed_opening()
    {
        var day = new DateOnly(2026, 10, 10);
        var perDayTotals = new Dictionary<DateOnly, (long, long)> { [day] = (0, 9_000) };

        var days = ConsolidationCalculator.CalculateDailyBreakdown(day, day, openingBalanceInCents: 1_000, perDayTotals);

        Assert.Single(days);
        Assert.Equal(1_000, days[0].OpeningBalanceInCents);
        Assert.Equal(-8_000, days[0].ClosingBalanceInCents); // INV-007: negative balance is allowed, not clamped
    }

    [Fact]
    public void CalculateDailyBreakdown_a_single_day_window_returns_exactly_one_entry()
    {
        var day = new DateOnly(2026, 10, 10);

        var days = ConsolidationCalculator.CalculateDailyBreakdown(
            day, day, openingBalanceInCents: 0, new Dictionary<DateOnly, (long, long)>());

        Assert.Single(days);
        Assert.Equal(day, days[0].BusinessDate);
    }
}
