namespace ConsolidationService.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef migrations`/`database update` at design time, so those commands never
/// have to build the whole host (JWT options, RabbitMQ, etc.) just to talk to the database.
/// </summary>
public sealed class ConsolidationDbContextFactory : IDesignTimeDbContextFactory<ConsolidationDbContext>
{
    public ConsolidationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__CONSOLIDATIONDB")
            ?? "Host=localhost;Port=5432;Database=consolidation_db;Username=cashflow;Password=cashflow-dev-only";

        var optionsBuilder = new DbContextOptionsBuilder<ConsolidationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new ConsolidationDbContext(optionsBuilder.Options);
    }
}
