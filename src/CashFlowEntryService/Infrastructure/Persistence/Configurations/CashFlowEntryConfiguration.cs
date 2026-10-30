namespace CashFlowEntryService.Infrastructure.Persistence.Configurations;

public sealed class CashFlowEntryConfiguration : IEntityTypeConfiguration<CashFlowEntry>
{
    /// <summary>Unique constraint name — relied upon to interpret Postgres 23505 violations.</summary>
    public const string IdempotencyKeyIndexName = "ux_cash_flow_entries_idempotency_key";

    public void Configure(EntityTypeBuilder<CashFlowEntry> builder)
    {
        builder.ToTable("cash_flow_entries", t =>
        {
            t.HasCheckConstraint("ck_cash_flow_entries_amount_positive", "amount_in_cents > 0");
            t.HasCheckConstraint("ck_cash_flow_entries_nature_valid", "nature IN ('Credit', 'Debit')");
        });

        builder.HasKey(l => l.EntryId);
        builder.Property(l => l.EntryId).HasColumnName("entry_id").ValueGeneratedNever();

        builder.Property(l => l.IdempotencyKey).HasColumnName("idempotency_key").IsRequired().HasMaxLength(200);
        builder.Property(l => l.RequestFingerprint).HasColumnName("request_fingerprint").IsRequired().HasMaxLength(64);
        builder.Property(l => l.AmountInCents).HasColumnName("amount_in_cents").IsRequired();
        builder.Property(l => l.Nature).HasColumnName("nature").HasConversion<string>().HasMaxLength(10).IsRequired();
        builder.Property(l => l.BusinessDate).HasColumnName("business_date").IsRequired();
        builder.Property(l => l.RecordedAtUtc).HasColumnName("recorded_at_utc").IsRequired();
        builder.Property(l => l.Description).HasColumnName("description").HasMaxLength(1000);
        builder.Property(l => l.PrincipalId).HasColumnName("principal_id").IsRequired().HasMaxLength(200);
        builder.Property(l => l.CorrelationId).HasColumnName("correlation_id").IsRequired().HasMaxLength(100);

        builder.HasIndex(l => l.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName(IdempotencyKeyIndexName);

        builder.HasIndex(l => new { l.BusinessDate, l.RecordedAtUtc })
            .HasDatabaseName("ix_cash_flow_entries_business_date");
    }
}
