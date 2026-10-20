namespace ConsolidationService.Infrastructure.Persistence.Configurations;

public sealed class ProjectionMetadataConfiguration : IEntityTypeConfiguration<ProjectionMetadata>
{
    public void Configure(EntityTypeBuilder<ProjectionMetadata> builder)
    {
        builder.ToTable("projection_metadata", t =>
            t.HasCheckConstraint("ck_projection_metadata_singleton", $"id = {ProjectionMetadata.SingletonId}"));

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(p => p.LastAppliedEventRecordedAtUtc).HasColumnName("last_applied_event_recorded_at_utc").IsRequired();
        builder.Property(p => p.LastAppliedAtUtc).HasColumnName("last_applied_at_utc").IsRequired();
        builder.Property(p => p.ProjectionVersion).HasColumnName("projection_version").IsRequired();
    }
}
