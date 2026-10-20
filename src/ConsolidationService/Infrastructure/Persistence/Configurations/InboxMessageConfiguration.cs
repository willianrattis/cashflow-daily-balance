namespace ConsolidationService.Infrastructure.Persistence.Configurations;

public sealed class InboxMessageConfiguration : IEntityTypeConfiguration<InboxMessage>
{
    public const string LaunchIdIndexName = "ux_inbox_launch_id";

    public void Configure(EntityTypeBuilder<InboxMessage> builder)
    {
        builder.ToTable("inbox_messages");

        builder.HasKey(i => i.EventId);
        builder.Property(i => i.EventId).HasColumnName("event_id").ValueGeneratedNever();

        builder.Property(i => i.LaunchId).HasColumnName("launch_id").IsRequired();
        builder.Property(i => i.EventType).HasColumnName("event_type").IsRequired().HasMaxLength(100);
        builder.Property(i => i.EventVersion).HasColumnName("event_version").IsRequired();
        builder.Property(i => i.ReceivedAtUtc).HasColumnName("received_at_utc").IsRequired();
        builder.Property(i => i.ProcessedAtUtc).HasColumnName("processed_at_utc");
        builder.Property(i => i.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(20).IsRequired();

        builder.HasIndex(i => i.LaunchId)
            .IsUnique()
            .HasDatabaseName(LaunchIdIndexName);
    }
}
