namespace ConsolidationService.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ topology shared by CashFlowEntryService (publisher) and ConsolidationService (consumer).
/// Both services declare it independently and idempotently on startup — there is no central
/// schema/definitions file, so `docker compose up` works with no manual broker setup
/// (docs/03-contratos.md, ADR-002).
///
/// Retry is modeled with per-level TTL "parking" queues that dead-letter back onto the main
/// queue once the delay elapses: fail once -> park level 1 -> fail again -> park level 2 ->
/// fail again -> park level 3. This bounded ladder applies only to *unexpected* failures, which
/// go to the DLQ after it; a *transient* failure (ADR-002) climbs the same ladder, then keeps
/// re-parking on the last level until it succeeds and never reaches the DLQ (<see cref="ConsumerFailureClassifier"/>). Queue names are
/// position-based (`retry.1|2|3`), not duration-based, because the delay of each level is
/// configurable (`RabbitMq:RetryDelaysMs`) — a name baked with a specific duration would lie the
/// moment an operator changes it. This keeps a bounded number of queues instead of requiring the
/// RabbitMQ delayed-message plugin.
/// </summary>
public static class CashFlowTopology
{
    public const string EventsExchange = "cashflow.events";
    public const string CashFlowEntryRecordedRoutingKey = "cashflow.entry.recorded.v1";
    public const string ConsolidationQueue = "consolidation.cashflow-entry-recorded.v1";
    public const string DeadLetterQueue = "consolidation.cashflow-entry-recorded.v1.dlq";
    private const string RetryQueuePrefix = "consolidation.cashflow-entry-recorded.v1.retry.";

    public const string RetryCountHeader = "x-retry-count";
    public const string FailureReasonHeader = "x-failure-reason";
    public const string CorrelationIdHeader = "x-correlation-id";

    /// <summary>Builds the retry ladder from configured delays — <c>retry.1</c> is the first level, not "5s".</summary>
    public static IReadOnlyList<(string QueueName, int DelayMilliseconds)> BuildRetryLevels(IReadOnlyList<int> delaysMs)
    {
        if (delaysMs is not { Count: > 0 })
        {
            throw new InvalidOperationException("RabbitMq:RetryDelaysMs deve ter ao menos um valor configurado.");
        }

        var levels = new List<(string QueueName, int DelayMilliseconds)>(delaysMs.Count);
        for (var i = 0; i < delaysMs.Count; i++)
        {
            levels.Add(($"{RetryQueuePrefix}{i + 1}", delaysMs[i]));
        }

        return levels;
    }
}
