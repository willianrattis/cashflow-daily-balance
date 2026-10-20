namespace ConsolidationService.IntegrationTests;

/// <summary>
/// ADR-004: two consumer instances racing on the exact same delivery (e.g. a redelivery that
/// reaches a second consumer before the first one's ACK lands) must apply it exactly once. Each
/// scope below stands in for one consumer instance — its own <see cref="ILaunchRecordedProjector"/>
/// resolution, its own scoped <c>ConsolidationDbContext</c> and transaction — racing on the same
/// `event_id`/`launch_id` unique constraints the real two-consumer scenario would race on too.
/// </summary>
[Collection(ConsolidationServiceCollection.Name)]
public class ConcurrentProjectionTests(ConsolidationServiceTestFixture fixture)
{
    [Fact]
    public async Task Two_consumers_racing_on_the_same_event_apply_it_exactly_once()
    {
        var launchId = Guid.NewGuid();
        var businessDate = UniqueBusinessDate.NextBlockStart();
        var envelope = new LaunchRecordedEnvelope(
            EventId: Guid.NewGuid(),
            EventType: LaunchRecordedEnvelope.SupportedEventType,
            EventVersion: LaunchRecordedEnvelope.SupportedVersion,
            OccurredAtUtc: DateTime.UtcNow,
            CorrelationId: "concurrent-test",
            CausationId: "concurrent-test",
            Payload: new LaunchRecordedPayload(launchId, 4321, "Credit", businessDate, DateTime.UtcNow));

        using var scopeA = fixture.Services.CreateScope();
        using var scopeB = fixture.Services.CreateScope();
        var projectorA = scopeA.ServiceProvider.GetRequiredService<ILaunchRecordedProjector>();
        var projectorB = scopeB.ServiceProvider.GetRequiredService<ILaunchRecordedProjector>();

        var results = await Task.WhenAll(
            projectorA.ProjectAsync(envelope, CancellationToken.None),
            projectorB.ProjectAsync(envelope, CancellationToken.None));

        Assert.Single(results, r => r == ProjectionOutcome.Processed);
        Assert.Single(results, r => r == ProjectionOutcome.Duplicate);

        await using var db = fixture.CreateDbContext();
        var inboxCount = await db.InboxMessages.CountAsync(i => i.EventId == envelope.EventId);
        Assert.Equal(1, inboxCount);

        var row = await db.DailyConsolidations.SingleAsync(d => d.BusinessDate == businessDate);
        Assert.Equal(4321, row.CreditsInCents);
        Assert.Equal(0, row.DebitsInCents);
    }
}
