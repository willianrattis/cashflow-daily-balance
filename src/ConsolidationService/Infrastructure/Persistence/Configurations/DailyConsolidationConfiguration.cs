namespace ConsolidationService.Infrastructure.Persistence.Configurations;

public sealed class DailyConsolidationConfiguration : IEntityTypeConfiguration<DailyConsolidation>
{
    public void Configure(EntityTypeBuilder<DailyConsolidation> builder)
    {
        builder.ToTable("daily_consolidations", t =>
        {
            t.HasCheckConstraint("ck_daily_credits_non_negative", "credits_in_cents >= 0");
            t.HasCheckConstraint("ck_daily_debits_non_negative", "debits_in_cents >= 0");
            t.HasCheckConstraint("ck_daily_net_change_consistent", "net_change_in_cents = credits_in_cents - debits_in_cents");
        });

        builder.HasKey(d => d.BusinessDate);
        builder.Property(d => d.BusinessDate).HasColumnName("business_date").IsRequired();
        builder.Property(d => d.CreditsInCents).HasColumnName("credits_in_cents").IsRequired();
        builder.Property(d => d.DebitsInCents).HasColumnName("debits_in_cents").IsRequired();
        builder.Property(d => d.NetChangeInCents).HasColumnName("net_change_in_cents").IsRequired();
        builder.Property(d => d.LastEventOccurredAtUtc).HasColumnName("last_event_occurred_at_utc").IsRequired();
        builder.Property(d => d.ProjectionVersion).HasColumnName("projection_version").IsRequired();
        builder.Property(d => d.UpdatedAtUtc).HasColumnName("updated_at_utc").IsRequired();
    }
}
