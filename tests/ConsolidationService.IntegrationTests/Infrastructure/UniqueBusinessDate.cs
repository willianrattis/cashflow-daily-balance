namespace ConsolidationService.IntegrationTests.Infrastructure;

/// <summary>
/// Hands out non-overlapping business-date blocks, each one strictly earlier than every block
/// handed out before it. The projection has a single global row per `business_date`, and the
/// whole test collection shares one Postgres container with no reset between tests, so two tests
/// landing on the same calendar day would silently add their totals together — or make a
/// `WaitUntilAsync` that checks "a row exists for this date" return immediately because an
/// earlier test already created it, before this test's own event was actually applied. Going
/// backwards in time also protects the opening-balance tests specifically: an opening balance is
/// a running sum of every row before a date, so if blocks only ever move further into the past,
/// a test's own block can never include a row written by a test that already ran (those all sit
/// at later dates) nor by one that hasn't run yet (it doesn't exist yet) — only its own rows ever
/// fall before its own `fromDate`. The anchor (year 2000) is safely before the real "today" the
/// query endpoint checks `toDate` against (so a block is never rejected as a future date), and
/// far from any literal date still used by display/validation-only tests (date format,
/// range-too-large, future-date checks) that never write to the projection.
/// </summary>
public static class UniqueBusinessDate
{
    private const int BlockSizeDays = 60;
    private static int _counter = -1;

    /// <summary>Start of a fresh block of <see cref="BlockSizeDays"/> days reserved for one test.</summary>
    public static DateOnly NextBlockStart() =>
        new DateOnly(2000, 1, 1).AddDays(-(Interlocked.Increment(ref _counter) * BlockSizeDays));
}
