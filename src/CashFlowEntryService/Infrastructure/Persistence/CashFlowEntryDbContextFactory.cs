namespace CashFlowEntryService.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef migrations`/`database update` at design time, so those commands never
/// have to build the whole host (JWT options, RabbitMQ, etc.) just to talk to the database.
/// </summary>
public sealed class CashFlowEntryDbContextFactory : IDesignTimeDbContextFactory<CashFlowEntryDbContext>
{
    public CashFlowEntryDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__ENTRYDB")
            ?? "Host=localhost;Port=5432;Database=cashflow_entry_db;Username=cashflow;Password=cashflow-dev-only";

        var optionsBuilder = new DbContextOptionsBuilder<CashFlowEntryDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new CashFlowEntryDbContext(optionsBuilder.Options);
    }
}
