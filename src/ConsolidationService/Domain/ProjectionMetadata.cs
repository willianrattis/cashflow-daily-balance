namespace ConsolidationService.Domain;

/// <summary>
/// Operational bookkeeping: when the last event was applied, the `recorded_at` of that event,
/// and a monotonic version counter. This is operational data, never a financial source
/// (docs/04-schema-logico.md). It powers `asOfUtc`, `lastAppliedEventRecordedAtUtc` and
/// `lastAppliedLagSeconds` in the consolidation query — the lag of the *last applied event*
/// (`LastAppliedAtUtc - LastAppliedEventRecordedAtUtc`), not time elapsed since now (NFR-005):
/// a quiet period with no recent entries is not stale by this measure. Exactly one row ever
/// exists — enforced by a constant primary key with a `CHECK` constraint
/// (<see cref="SingletonId"/>, <c>ProjectionMetadataConfiguration</c>).
/// </summary>
public sealed class ProjectionMetadata
{
    public const int SingletonId = 1;

    private ProjectionMetadata() { }

    public int Id { get; private set; } = SingletonId;
    public DateTime LastAppliedEventRecordedAtUtc { get; private set; }
    public DateTime LastAppliedAtUtc { get; private set; }
    public long ProjectionVersion { get; private set; }
}
