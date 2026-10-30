namespace ConsolidationService.Infrastructure.Projection;

/// <summary>
/// Default <see cref="ICashFlowEntryRecordedProjector"/>: Inbox insert + daily projection upsert in the
/// same local transaction, so the consumer can ACK only after both commit together.
/// </summary>
public sealed class CashFlowEntryRecordedProjector(ConsolidationDbContext db) : ICashFlowEntryRecordedProjector
{
    public async Task<ProjectionOutcome> ProjectAsync(CashFlowEntryRecordedEnvelope envelope, CancellationToken ct)
    {
        var payload = envelope.Payload!;
        var receivedAtUtc = DateTime.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var inbox = InboxMessage.CreateProcessed(
            envelope.EventId, payload.EntryId, envelope.EventType, envelope.EventVersion,
            receivedAtUtc, receivedAtUtc);
        db.InboxMessages.Add(inbox);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.TryGetUniqueViolationConstraint(out _))
        {
            await transaction.RollbackAsync(ct);
            return ProjectionOutcome.Duplicate;
        }

        var isCredit = string.Equals(payload.Nature, "Credit", StringComparison.Ordinal);
        var creditsDelta = isCredit ? payload.AmountInCents : 0;
        var debitsDelta = isCredit ? 0 : payload.AmountInCents;
        var netDelta = creditsDelta - debitsDelta;

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO daily_consolidations
                 (business_date, credits_in_cents, debits_in_cents, net_change_in_cents, last_event_occurred_at_utc, projection_version, updated_at_utc)
             VALUES
                 ({payload.BusinessDate}, {creditsDelta}, {debitsDelta}, {netDelta}, {payload.RecordedAtUtc}, 1, {receivedAtUtc})
             ON CONFLICT (business_date) DO UPDATE SET
                 credits_in_cents = daily_consolidations.credits_in_cents + EXCLUDED.credits_in_cents,
                 debits_in_cents = daily_consolidations.debits_in_cents + EXCLUDED.debits_in_cents,
                 net_change_in_cents = daily_consolidations.net_change_in_cents + EXCLUDED.net_change_in_cents,
                 last_event_occurred_at_utc = GREATEST(daily_consolidations.last_event_occurred_at_utc, EXCLUDED.last_event_occurred_at_utc),
                 projection_version = daily_consolidations.projection_version + 1,
                 updated_at_utc = EXCLUDED.updated_at_utc
             """,
            ct);

        await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO projection_metadata (id, last_applied_event_recorded_at_utc, last_applied_at_utc, projection_version)
             VALUES ({ProjectionMetadata.SingletonId}, {payload.RecordedAtUtc}, {receivedAtUtc}, 1)
             ON CONFLICT (id) DO UPDATE SET
                 last_applied_event_recorded_at_utc = EXCLUDED.last_applied_event_recorded_at_utc,
                 last_applied_at_utc = EXCLUDED.last_applied_at_utc,
                 projection_version = projection_metadata.projection_version + 1
             """,
            ct);

        await transaction.CommitAsync(ct);
        return ProjectionOutcome.Processed;
    }
}
