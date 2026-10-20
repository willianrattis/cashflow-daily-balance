namespace ConsolidationService.Infrastructure.Persistence;

/// <summary>
/// Owns `inbox_messages`, `daily_consolidations` and `projection_metadata` exclusively
/// (docs/04-schema-logico.md). No other service is allowed to read or write this database directly.
/// </summary>
public sealed class ConsolidationDbContext(DbContextOptions<ConsolidationDbContext> options) : DbContext(options)
{
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<DailyConsolidation> DailyConsolidations => Set<DailyConsolidation>();
    public DbSet<ProjectionMetadata> ProjectionMetadata => Set<ProjectionMetadata>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ConsolidationDbContext).Assembly);
    }
}
