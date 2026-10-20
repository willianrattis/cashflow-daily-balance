namespace ConsolidationService.Infrastructure.Observability;

/// <summary>Essential metrics required by docs/05-aspectos-transversais.md (registered as a singleton).</summary>
public sealed class ConsolidationServiceMetrics
{
    public const string MeterName = "CashFlow.ConsolidationService";

    private readonly Meter _meter;

    public Counter<long> ConsumerEventsTotal { get; }
    public Histogram<double> ConsumerLagSeconds { get; }
    public Counter<long> ConsolidationRequestsTotal { get; }
    public Histogram<double> ConsolidationQueryDurationMs { get; }
    public Counter<long> DlqMessagesTotal { get; }

    public ConsolidationServiceMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        ConsumerEventsTotal = _meter.CreateCounter<long>(
            "consumer_events_total",
            description: "Total de eventos consumidos, por outcome " +
                "(processed/duplicate/rejected/retry_transient/retry_unexpected/dead_lettered).");
        ConsolidationRequestsTotal = _meter.CreateCounter<long>(
            "consolidation_requests_total",
            description: "Total de requisições de consulta do Consolidado, por outcome (success/validation/unavailable/error).");
        // This is what NFR-005 measures globally: applied_at - recorded_at for every event
        // (covers Outbox wait + broker + consumer), not "time since last sale".
        // Bucket boundaries tuned for the 5s/30s targets (MetricsExporterExtensions AddView).
        ConsumerLagSeconds = _meter.CreateHistogram<double>(
            "consumer_lag_seconds", unit: "s", description: "Atraso de projeção ponta a ponta: applied_at - recorded_at (NFR-005).");
        ConsolidationQueryDurationMs = _meter.CreateHistogram<double>(
            "consolidation_query_duration_ms", unit: "ms", description: "Duração da consulta do Consolidado.");
        DlqMessagesTotal = _meter.CreateCounter<long>(
            "dlq_messages_total", description: "Mensagens enviadas para a Dead Letter Queue.");
    }
}
