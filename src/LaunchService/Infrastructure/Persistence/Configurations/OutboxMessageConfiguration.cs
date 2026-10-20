namespace LaunchService.Infrastructure.Persistence.Configurations;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");

        builder.HasKey(o => o.OutboxId);
        builder.Property(o => o.OutboxId).HasColumnName("outbox_id").ValueGeneratedNever();

        builder.Property(o => o.AggregateId).HasColumnName("aggregate_id").IsRequired();
        builder.Property(o => o.EventId).HasColumnName("event_id").IsRequired();
        builder.Property(o => o.EventType).HasColumnName("event_type").IsRequired().HasMaxLength(100);
        builder.Property(o => o.EventVersion).HasColumnName("event_version").IsRequired();
        builder.Property(o => o.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(o => o.OccurredAtUtc).HasColumnName("occurred_at_utc").IsRequired();
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(o => o.AttemptCount).HasColumnName("attempt_count").IsRequired();
        builder.Property(o => o.NextAttemptAtUtc).HasColumnName("next_attempt_at_utc");
        builder.Property(o => o.PublishedAtUtc).HasColumnName("published_at_utc");
        builder.Property(o => o.LastError).HasColumnName("last_error").HasMaxLength(2000);
        builder.Property(o => o.CreatedAtUtc).HasColumnName("created_at_utc").IsRequired();

        builder.HasIndex(o => o.EventId).IsUnique().HasDatabaseName("ux_outbox_event_id");

        builder.HasIndex(o => new { o.AggregateId, o.EventType, o.EventVersion })
            .IsUnique()
            .HasDatabaseName("ux_outbox_aggregate_event");

        builder.HasIndex(o => new { o.Status, o.NextAttemptAtUtc })
            .HasDatabaseName("ix_outbox_pending")
            .HasFilter("status <> 'Published'");
    }
}
