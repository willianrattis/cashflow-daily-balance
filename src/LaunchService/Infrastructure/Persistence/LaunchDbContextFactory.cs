namespace LaunchService.Infrastructure.Persistence;

/// <summary>
/// Used only by `dotnet ef migrations`/`database update` at design time, so those commands never
/// have to build the whole host (JWT options, RabbitMQ, etc.) just to talk to the database.
/// </summary>
public sealed class LaunchDbContextFactory : IDesignTimeDbContextFactory<LaunchDbContext>
{
    public LaunchDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CONNECTIONSTRINGS__LAUNCHDB")
            ?? "Host=localhost;Port=5432;Database=launch_db;Username=cashflow;Password=cashflow-dev-only";

        var optionsBuilder = new DbContextOptionsBuilder<LaunchDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new LaunchDbContext(optionsBuilder.Options);
    }
}
