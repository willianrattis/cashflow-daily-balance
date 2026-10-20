namespace LaunchService.Infrastructure.Persistence;

/// <summary>
/// Owns `launches` and `outbox_messages` exclusively (docs/04-schema-logico.md). No other service
/// is allowed to read or write this database directly.
/// </summary>
public sealed class LaunchDbContext(DbContextOptions<LaunchDbContext> options) : DbContext(options)
{
    public DbSet<Launch> Launches => Set<Launch>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(LaunchDbContext).Assembly);
    }
}
