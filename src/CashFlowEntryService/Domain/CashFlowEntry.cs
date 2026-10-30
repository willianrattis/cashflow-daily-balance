namespace CashFlowEntryService.Domain;

/// <summary>
/// An immutable financial fact: a credit or debit entry. Entries are append-only (INV-001) —
/// there is intentionally no setter that mutates amount/nature/date after construction.
/// </summary>
public sealed class CashFlowEntry
{
    private CashFlowEntry() { }

    public Guid EntryId { get; private set; }
    public string IdempotencyKey { get; private set; } = default!;
    public string RequestFingerprint { get; private set; } = default!;
    public long AmountInCents { get; private set; }
    public EntryNature Nature { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string? Description { get; private set; }
    public string PrincipalId { get; private set; } = default!;
    public string CorrelationId { get; private set; } = default!;

    public static CashFlowEntry CreateNew(
        string idempotencyKey,
        long amountInCents,
        EntryNature nature,
        string? description,
        DateOnly businessDate,
        DateTime recordedAtUtc,
        string principalId,
        string correlationId)
    {
        if (amountInCents <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amountInCents), amountInCents, "O valor deve ser positivo.");
        }

        var fingerprint = EntryFingerprint.Compute(amountInCents, nature, description);

        return new CashFlowEntry
        {
            EntryId = Guid.NewGuid(),
            IdempotencyKey = idempotencyKey,
            RequestFingerprint = fingerprint,
            AmountInCents = amountInCents,
            Nature = nature,
            BusinessDate = businessDate,
            RecordedAtUtc = recordedAtUtc,
            Description = description,
            PrincipalId = principalId,
            CorrelationId = correlationId
        };
    }
}
