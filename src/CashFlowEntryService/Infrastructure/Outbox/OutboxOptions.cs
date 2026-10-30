namespace CashFlowEntryService.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>
    /// Rows claimed per publish cycle. Also doubles as the drain signal: when a batch comes back
    /// full, <see cref="OutboxPublisherService"/> assumes more rows are waiting and fetches the
    /// next batch immediately instead of sleeping.
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>Delay between cycles when the last batch was partial or empty.</summary>
    public int PollIntervalMs { get; set; } = 500;

    /// <summary>
    /// Bounds a single message's publish+confirm (including establishing the broker connection
    /// if it does not exist yet) via a linked <see cref="CancellationToken"/>. A timeout is
    /// classified the same as any other connection-level failure (<c>reason=connection</c>) and
    /// short-circuits the rest of the claimed batch instead of letting every remaining row pay
    /// for its own doomed attempt (ADR-003).
    /// </summary>
    public int PublishTimeoutMs { get; set; } = 5000;
}
