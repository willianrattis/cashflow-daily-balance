namespace CashFlowEntryService.Infrastructure.Persistence;

/// <summary>
/// Owns `cash_flow_entries` and `outbox_messages` exclusively (docs/04-schema-logico.md). No other service
/// is allowed to read or write this database directly.
/// </summary>
public sealed class CashFlowEntryDbContext(DbContextOptions<CashFlowEntryDbContext> options) : DbContext(options)
{
    public DbSet<CashFlowEntry> Entries => Set<CashFlowEntry>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CashFlowEntryDbContext).Assembly);
    }
}
