namespace LaunchService.Infrastructure.Observability;

/// <summary>Essential metrics required by docs/05-aspectos-transversais.md (registered as a singleton).</summary>
public sealed class LaunchServiceMetrics
{
    public const string MeterName = "CashFlow.LaunchService";

    private readonly Meter _meter;
    private long _outboxPendingCount;
    private double _outboxOldestAgeSeconds;

    public Counter<long> LaunchRequestsTotal { get; }
    public Histogram<double> LaunchRequestDurationMs { get; }
    public Counter<long> BrokerPublishFailuresTotal { get; }
    public Counter<long> OutboxPublishedTotal { get; }
    public Histogram<double> OutboxPublishBatchDurationMs { get; }

    public LaunchServiceMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        LaunchRequestsTotal = _meter.CreateCounter<long>(
            "launch_requests_total", description: "Total de requisições de registro de lançamento, por outcome.");
        LaunchRequestDurationMs = _meter.CreateHistogram<double>(
            "launch_request_duration_ms", unit: "ms", description: "Duração do caminho transacional de registro.");
        BrokerPublishFailuresTotal = _meter.CreateCounter<long>(
            "broker_publish_failures_total", description: "Falhas ao publicar LaunchRecorded.v1 no broker.");
        OutboxPublishedTotal = _meter.CreateCounter<long>(
            "outbox_published_total", description: "Total de eventos publicados com sucesso pelo Outbox Publisher.");
        OutboxPublishBatchDurationMs = _meter.CreateHistogram<double>(
            "outbox_publish_batch_duration_ms", unit: "ms",
            description: "Duração de um ciclo de claim (SELECT ... FOR UPDATE SKIP LOCKED) + publish + commit.");

        _meter.CreateObservableGauge(
            "outbox_pending_count",
            () => Interlocked.Read(ref _outboxPendingCount),
            description: "Mensagens do Outbox ainda não publicadas.");
        _meter.CreateObservableGauge(
            "outbox_oldest_age_seconds",
            () => _outboxOldestAgeSeconds,
            unit: "s",
            description: "Idade da mensagem pendente mais antiga do Outbox.");
    }

    public void ReportOutboxBacklog(long pendingCount, double oldestAgeSeconds)
    {
        Interlocked.Exchange(ref _outboxPendingCount, pendingCount);
        _outboxOldestAgeSeconds = oldestAgeSeconds;
    }
}
