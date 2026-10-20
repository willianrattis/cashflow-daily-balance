namespace ConsolidationService.IntegrationTests;

/// <summary>
/// The projection is additive by business date (ADR-005, ADR-008), so arrival order can never
/// corrupt a daily total — but `projection_metadata.last_applied_event_recorded_at_utc` is a
/// plain "last write wins" column (<see cref="LaunchRecordedProjector"/>'s raw SQL upsert), not a
/// running maximum: it reflects whichever event was *applied* most recently, which under
/// out-of-order delivery is not necessarily the one with the latest `recordedAtUtc` (A-0xx,
/// docs/01-requisitos-e-assumptions.md). This test locks in both halves of that trade-off.
/// </summary>
[Collection(ConsolidationServiceCollection.Name)]
public class OutOfOrderDeliveryTests(ConsolidationServiceTestFixture fixture)
{
    [Fact]
    public async Task Out_of_order_delivery_keeps_daily_totals_correct_but_moves_lastAppliedEventRecordedAtUtc_backward()
    {
        var earlierDate = UniqueBusinessDate.NextBlockStart();
        var laterDate = earlierDate.AddDays(1);
        var baseline = DateTime.UtcNow;

        var olderEvent = new LaunchRecordedEnvelope(
            EventId: Guid.NewGuid(),
            EventType: LaunchRecordedEnvelope.SupportedEventType,
            EventVersion: LaunchRecordedEnvelope.SupportedVersion,
            OccurredAtUtc: baseline,
            CorrelationId: "out-of-order-test",
            CausationId: "out-of-order-test",
            Payload: new LaunchRecordedPayload(Guid.NewGuid(), 1_000, "Credit", earlierDate, baseline));

        var newerEvent = new LaunchRecordedEnvelope(
            EventId: Guid.NewGuid(),
            EventType: LaunchRecordedEnvelope.SupportedEventType,
            EventVersion: LaunchRecordedEnvelope.SupportedVersion,
            OccurredAtUtc: baseline.AddSeconds(30),
            CorrelationId: "out-of-order-test",
            CausationId: "out-of-order-test",
            Payload: new LaunchRecordedPayload(Guid.NewGuid(), 2_000, "Debit", laterDate, baseline.AddSeconds(30)));

        // Apply the NEWER event first, then the OLDER one — the redelivery/retry/replay paths
        // (ADR-002, docs/diagrams/08-sequence-dlq-replay.md) never guarantee the opposite.
        var newerOutcome = await ProjectDirectlyAsync(newerEvent);
        var olderOutcome = await ProjectDirectlyAsync(olderEvent);

        Assert.Equal(ProjectionOutcome.Processed, newerOutcome);
        Assert.Equal(ProjectionOutcome.Processed, olderOutcome);

        await using var db = fixture.CreateDbContext();
        var earlierRow = await db.DailyConsolidations.SingleAsync(d => d.BusinessDate == earlierDate);
        Assert.Equal(1_000, earlierRow.CreditsInCents);

        var laterRow = await db.DailyConsolidations.SingleAsync(d => d.BusinessDate == laterDate);
        Assert.Equal(2_000, laterRow.DebitsInCents);

        var metadata = await db.ProjectionMetadata.SingleAsync();

        // The LAST applied event was the older one (recordedAtUtc = baseline), even though an
        // event with a later recordedAtUtc (baseline + 30s) was applied first — the field moved
        // backward, exactly the non-monotonic behavior this test documents.
        Assert.Equal(olderEvent.Payload!.RecordedAtUtc, metadata.LastAppliedEventRecordedAtUtc, TimeSpan.FromMilliseconds(1));
        Assert.True(
            metadata.LastAppliedEventRecordedAtUtc < newerEvent.Payload!.RecordedAtUtc,
            "lastAppliedEventRecordedAtUtc deveria ter regredido após a entrega fora de ordem.");
    }

    private async Task<ProjectionOutcome> ProjectDirectlyAsync(LaunchRecordedEnvelope envelope)
    {
        using var scope = fixture.Services.CreateScope();
        var projector = scope.ServiceProvider.GetRequiredService<ILaunchRecordedProjector>();
        return await projector.ProjectAsync(envelope, CancellationToken.None);
    }
}
