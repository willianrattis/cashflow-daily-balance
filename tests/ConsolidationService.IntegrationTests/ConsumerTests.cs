namespace ConsolidationService.IntegrationTests;

[Collection(ConsolidationServiceCollection.Name)]
public class ConsumerTests(ConsolidationServiceTestFixture fixture)
{
    [Fact]
    public async Task A_published_event_is_consumed_and_projected_onto_the_daily_total()
    {
        var entryId = Guid.NewGuid();
        var businessDate = UniqueBusinessDate.NextBlockStart();

        await TestEventPublisher.PublishCashFlowEntryRecordedAsync(
            fixture, entryId, amountInCents: 1500, nature: "Credit", businessDate, DateTime.UtcNow);

        var projected = await Eventually.WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            return await db.DailyConsolidations.AnyAsync(d => d.BusinessDate == businessDate);
        });

        Assert.True(projected, "A projeção diária deveria existir após o consumo do evento.");

        await using var finalDb = fixture.CreateDbContext();
        var row = await finalDb.DailyConsolidations.SingleAsync(d => d.BusinessDate == businessDate);
        Assert.Equal(1500, row.CreditsInCents);
        Assert.Equal(0, row.DebitsInCents);
        Assert.Equal(1500, row.NetChangeInCents);

        var inboxExists = await finalDb.InboxMessages.AnyAsync(i => i.EntryId == entryId);
        Assert.True(inboxExists);
    }

    [Fact]
    public async Task Redelivering_the_same_event_does_not_apply_the_value_twice()
    {
        var entryId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var businessDate = UniqueBusinessDate.NextBlockStart();

        await TestEventPublisher.PublishCashFlowEntryRecordedAsync(
            fixture, entryId, amountInCents: 2000, nature: "Debit", businessDate, DateTime.UtcNow, eventId: eventId);

        await Eventually.WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            return await db.DailyConsolidations.AnyAsync(d => d.BusinessDate == businessDate);
        });

        // Redeliver the exact same event (same EventId/EntryId) — simulates an at-least-once redelivery.
        await TestEventPublisher.PublishCashFlowEntryRecordedAsync(
            fixture, entryId, amountInCents: 2000, nature: "Debit", businessDate, DateTime.UtcNow, eventId: eventId);

        // Give the (no-op) redelivery time to be processed before asserting the total stayed the same.
        await Task.Delay(TimeSpan.FromSeconds(2));

        await using var db = fixture.CreateDbContext();
        var row = await db.DailyConsolidations.SingleAsync(d => d.BusinessDate == businessDate);
        Assert.Equal(2000, row.DebitsInCents);

        var inboxCount = await db.InboxMessages.CountAsync(i => i.EntryId == entryId);
        Assert.Equal(1, inboxCount);
    }

    [Fact]
    public async Task An_invalid_event_reaches_the_dead_letter_queue_without_crashing_the_consumer()
    {
        // Missing required "payload" and using an unsupported event type: must be rejected outright.
        var invalidJson = $$"""
            {
              "eventId": "{{Guid.NewGuid()}}",
              "eventType": "SomethingElse",
              "eventVersion": 1,
              "occurredAtUtc": "{{DateTime.UtcNow:O}}",
              "correlationId": "c",
              "causationId": "c"
            }
            """;

        await TestEventPublisher.PublishRawAsync(fixture, invalidJson);

        var dlqMessage = await TestBrokerAdmin.WaitForMessageAsync(fixture, CashFlowTopology.DeadLetterQueue, TimeSpan.FromSeconds(10));
        Assert.NotNull(dlqMessage);
        var reason = ReadHeaderString(dlqMessage!, CashFlowTopology.FailureReasonHeader);
        Assert.Equal("unsupported_event_type", reason);

        // The consumer must keep working for subsequent, valid events after rejecting the bad one.
        var entryId = Guid.NewGuid();
        var businessDate = UniqueBusinessDate.NextBlockStart();
        await TestEventPublisher.PublishCashFlowEntryRecordedAsync(
            fixture, entryId, amountInCents: 750, nature: "Credit", businessDate, DateTime.UtcNow);

        var projected = await Eventually.WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            return await db.DailyConsolidations.AnyAsync(d => d.BusinessDate == businessDate);
        });

        Assert.True(projected, "Um evento inválido anterior não deveria impedir o processamento de eventos válidos subsequentes.");
    }

    [Fact]
    public async Task A_message_that_reaches_the_dlq_keeps_the_original_correlation_id_header_and_body()
    {
        // Same shape as the unsupported-event-type DLQ case above, but carrying an AMQP
        // x-correlation-id header the way CashFlowEntryService's Outbox Publisher stamps it on first
        // publish — proves the republish onto the DLQ copies it instead of dropping it.
        var expectedCorrelationId = $"corr-{Guid.NewGuid():N}";
        var invalidJson = $$"""
            {
              "eventId": "{{Guid.NewGuid()}}",
              "eventType": "SomethingElse",
              "eventVersion": 1,
              "occurredAtUtc": "{{DateTime.UtcNow:O}}",
              "correlationId": "{{expectedCorrelationId}}",
              "causationId": "c"
            }
            """;

        await TestEventPublisher.PublishRawAsync(fixture, invalidJson, amqpCorrelationIdHeader: expectedCorrelationId);

        var dlqMessage = await TestBrokerAdmin.WaitForMessageAsync(fixture, CashFlowTopology.DeadLetterQueue, TimeSpan.FromSeconds(10));
        Assert.NotNull(dlqMessage);

        var headerCorrelationId = ReadHeaderString(dlqMessage!, CashFlowTopology.CorrelationIdHeader);
        Assert.Equal(expectedCorrelationId, headerCorrelationId);

        using var bodyEnvelope = JsonDocument.Parse(dlqMessage!.Body);
        Assert.Equal(expectedCorrelationId, bodyEnvelope.RootElement.GetProperty("correlationId").GetString());
    }

    [Fact]
    public async Task An_event_with_a_default_businessDate_reaches_the_dlq_as_invalid_business_date()
    {
        var invalidJson = $$"""
            {
              "eventId": "{{Guid.NewGuid()}}",
              "eventType": "CashFlowEntryRecorded",
              "eventVersion": 1,
              "occurredAtUtc": "{{DateTime.UtcNow:O}}",
              "correlationId": "c",
              "causationId": "c",
              "payload": {
                "entryId": "{{Guid.NewGuid()}}",
                "amountInCents": 100,
                "nature": "Credit",
                "businessDate": "0001-01-01",
                "recordedAtUtc": "{{DateTime.UtcNow:O}}"
              }
            }
            """;

        await TestEventPublisher.PublishRawAsync(fixture, invalidJson);

        var dlqMessage = await TestBrokerAdmin.WaitForMessageAsync(fixture, CashFlowTopology.DeadLetterQueue, TimeSpan.FromSeconds(10));
        Assert.NotNull(dlqMessage);
        var reason = ReadHeaderString(dlqMessage!, CashFlowTopology.FailureReasonHeader);
        Assert.Equal("invalid_business_date", reason);
    }

    [Fact]
    public async Task An_event_with_a_default_recordedAtUtc_reaches_the_dlq_as_invalid_recorded_at()
    {
        var invalidJson = $$"""
            {
              "eventId": "{{Guid.NewGuid()}}",
              "eventType": "CashFlowEntryRecorded",
              "eventVersion": 1,
              "occurredAtUtc": "{{DateTime.UtcNow:O}}",
              "correlationId": "c",
              "causationId": "c",
              "payload": {
                "entryId": "{{Guid.NewGuid()}}",
                "amountInCents": 100,
                "nature": "Credit",
                "businessDate": "2026-10-16",
                "recordedAtUtc": "0001-01-01T00:00:00Z"
              }
            }
            """;

        await TestEventPublisher.PublishRawAsync(fixture, invalidJson);

        var dlqMessage = await TestBrokerAdmin.WaitForMessageAsync(fixture, CashFlowTopology.DeadLetterQueue, TimeSpan.FromSeconds(10));
        Assert.NotNull(dlqMessage);
        var reason = ReadHeaderString(dlqMessage!, CashFlowTopology.FailureReasonHeader);
        Assert.Equal("invalid_recorded_at", reason);
    }

    [Fact]
    public async Task A_transient_fault_keeps_retrying_and_eventually_succeeds_without_ever_reaching_the_dlq()
    {
        var entryId = Guid.NewGuid();
        var businessDate = UniqueBusinessDate.NextBlockStart();

        // More failures than retry levels exist (3), so this also proves the transient path
        // re-parks on the last level instead of dead-lettering once the ladder is exhausted (ADR-002).
        fixture.Faults.FailNTimes(entryId, times: 6, () => new TimeoutException("simulated transient Postgres timeout"));

        await TestEventPublisher.PublishCashFlowEntryRecordedAsync(
            fixture, entryId, amountInCents: 1234, nature: "Credit", businessDate, DateTime.UtcNow);

        var projected = await Eventually.WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            return await db.DailyConsolidations.AnyAsync(d => d.BusinessDate == businessDate);
        }, timeout: TimeSpan.FromSeconds(30));

        Assert.True(projected, "A projeção deveria ser aplicada assim que a falha transitória parasse de ocorrer.");

        await using var db = fixture.CreateDbContext();
        var row = await db.DailyConsolidations.SingleAsync(d => d.BusinessDate == businessDate);
        Assert.Equal(1234, row.CreditsInCents);

        var inboxCount = await db.InboxMessages.CountAsync(i => i.EntryId == entryId);
        Assert.Equal(1, inboxCount);

        var dlqMessage = await TestBrokerAdmin.BasicGetAsync(fixture, CashFlowTopology.DeadLetterQueue, autoAck: false);
        Assert.Null(dlqMessage);
    }

    [Fact]
    public async Task An_unexpected_fault_that_never_recovers_reaches_the_dlq_after_the_bounded_ladder()
    {
        var entryId = Guid.NewGuid();
        var businessDate = UniqueBusinessDate.NextBlockStart();

        fixture.Faults.FailAlways(entryId, () => new InvalidOperationException("simulated unexpected projector bug"));

        await TestEventPublisher.PublishCashFlowEntryRecordedAsync(
            fixture, entryId, amountInCents: 999, nature: "Debit", businessDate, DateTime.UtcNow);

        var dlqMessage = await TestBrokerAdmin.WaitForMessageAsync(fixture, CashFlowTopology.DeadLetterQueue, TimeSpan.FromSeconds(15));
        Assert.NotNull(dlqMessage);
        var reason = ReadHeaderString(dlqMessage!, CashFlowTopology.FailureReasonHeader);
        Assert.StartsWith("unexpected_failure:", reason);

        await using var db = fixture.CreateDbContext();
        var projected = await db.DailyConsolidations.AnyAsync(d => d.BusinessDate == businessDate);
        Assert.False(projected, "Uma falha inesperada que nunca se recupera não deveria ter alterado a projeção.");
    }

    [Fact]
    public async Task Replaying_a_dead_lettered_message_restarts_the_retry_ladder_from_zero()
    {
        var entryId = Guid.NewGuid();
        var businessDate = UniqueBusinessDate.NextBlockStart();

        fixture.Faults.FailAlways(entryId, () => new InvalidOperationException("simulated unexpected projector bug"));

        await TestEventPublisher.PublishCashFlowEntryRecordedAsync(
            fixture, entryId, amountInCents: 4200, nature: "Debit", businessDate, DateTime.UtcNow);

        var dlqMessage = await TestBrokerAdmin.WaitForMessageAsync(fixture, CashFlowTopology.DeadLetterQueue, TimeSpan.FromSeconds(15));
        Assert.NotNull(dlqMessage);

        // A DLQ'd message never carries x-retry-count (CashFlowEntryRecordedConsumerService always
        // passes retryCountHeaderValue: null for the DLQ) — replay-dlq.sh's shovel moves it back
        // onto the main exchange with its properties as-is, so the redelivered copy arrives the
        // same way: no header at all, read as retryCount 0 (ReadRetryCount's default).
        Assert.False(
            dlqMessage!.BasicProperties.Headers?.ContainsKey(CashFlowTopology.RetryCountHeader) ?? false,
            "Uma mensagem na DLQ não deveria carregar x-retry-count.");
        var replayedBody = Encoding.UTF8.GetString(dlqMessage.Body.Span);

        // Simulate the replay: three MORE unexpected failures before succeeding. If the ladder
        // had not reset to zero, a single failure here would dead-letter it again immediately
        // instead of going through all three retry levels again.
        fixture.Faults.FailNTimes(entryId, times: 3, () => new InvalidOperationException("simulated unexpected projector bug — after replay"));
        await TestEventPublisher.PublishRawAsync(fixture, replayedBody);

        var projected = await Eventually.WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            return await db.DailyConsolidations.AnyAsync(d => d.BusinessDate == businessDate);
        }, timeout: TimeSpan.FromSeconds(30));

        Assert.True(projected, "A projeção deveria ser aplicada depois que o replay esgotasse a nova escada de retries.");

        await using var finalDb = fixture.CreateDbContext();
        var row = await finalDb.DailyConsolidations.SingleAsync(d => d.BusinessDate == businessDate);
        Assert.Equal(4200, row.DebitsInCents);

        var secondDlqMessage = await TestBrokerAdmin.BasicGetAsync(fixture, CashFlowTopology.DeadLetterQueue, autoAck: false);
        Assert.Null(secondDlqMessage);
    }

    private static string? ReadHeaderString(RabbitMQ.Client.BasicGetResult message, string headerName)
    {
        if (message.BasicProperties.Headers is null
            || !message.BasicProperties.Headers.TryGetValue(headerName, out var raw))
        {
            return null;
        }

        return raw switch
        {
            string s => s,
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            _ => raw?.ToString()
        };
    }
}
