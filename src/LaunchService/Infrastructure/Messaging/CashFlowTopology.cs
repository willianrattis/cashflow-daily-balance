namespace LaunchService.Infrastructure.Messaging;

/// <summary>
/// RabbitMQ topology shared by LaunchService (publisher) and ConsolidationService (consumer).
/// Both services declare it independently and idempotently on startup — there is no central
/// schema/definitions file, so `docker compose up` works with no manual broker setup
/// (docs/03-contratos.md, ADR-002).
///
/// Retry is modeled with per-level TTL "parking" queues that dead-letter back onto the main
/// queue once the delay elapses. LaunchService never declares or touches these retry/DLQ
/// queues, and never needs their names or delays — topology ownership, including the retry
/// ladder itself, stays entirely with the consumer (ADR-003). <see cref="ConsolidationQueue"/>
/// is only the publish-side name of the main queue (used by this project's own test fixtures to
/// simulate the consumer side); <see cref="EventsExchange"/>, <see cref="LaunchRecordedRoutingKey"/>
/// and <see cref="RetryCountHeader"/> (set to 0 on first publish) are the members actually used
/// by production code in this service. See
/// ConsolidationService.Infrastructure.Messaging.CashFlowTopology for the retry ladder
/// (`RabbitMq:RetryDelaysMs`), built at runtime from configuration.
/// </summary>
public static class CashFlowTopology
{
    public const string EventsExchange = "cashflow.events";
    public const string LaunchRecordedRoutingKey = "launch.recorded.v1";
    public const string ConsolidationQueue = "consolidation.launch-recorded.v1";

    public const string RetryCountHeader = "x-retry-count";
    public const string CorrelationIdHeader = "x-correlation-id";
}
