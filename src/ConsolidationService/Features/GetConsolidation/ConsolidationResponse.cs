namespace ConsolidationService.Features.GetConsolidation;

/// <summary>One calendar day's totals within <see cref="ConsolidationResponse.Days"/> (BR-02).</summary>
public sealed record DailyBreakdownResponse(
    DateOnly BusinessDate,
    long OpeningBalanceInCents,
    long CreditsInCents,
    long DebitsInCents,
    long NetChangeInCents,
    long ClosingBalanceInCents);

/// <summary>
/// Response shape required by docs/03-contratos.md. <see cref="AsOfUtc"/>,
/// <see cref="LastAppliedEventRecordedAtUtc"/> and <see cref="LastAppliedLagSeconds"/> describe
/// the projection's own staleness (NFR-005) — a quiet period with no recent entries is not
/// stale by this measure; system-level backlog visibility (queue depth, Outbox age, consumer
/// lag) lives in metrics/dashboards, not in this response. <see cref="Days"/> is the day-by-day
/// breakdown the challenge PDF asks for ("relatório que disponibilize o saldo diário
/// consolidado") — one entry per calendar day in the window, including gap days (BR-02).
/// </summary>
public sealed record ConsolidationResponse(
    DateOnly FromDate,
    DateOnly ToDate,
    DateOnly? FirstEntryDate,
    long OpeningBalanceInCents,
    long CreditsInCents,
    long DebitsInCents,
    long NetChangeInCents,
    long ClosingBalanceInCents,
    IReadOnlyList<DailyBreakdownResponse> Days,
    DateTime AsOfUtc,
    DateTime? LastAppliedEventRecordedAtUtc,
    double? LastAppliedLagSeconds,
    long ProjectionVersion);
