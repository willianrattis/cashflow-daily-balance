namespace ConsolidationService.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConsolidationDbConnectionString();

        services.AddDbContext<ConsolidationDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }

    public static string GetConsolidationDbConnectionString(this IConfiguration configuration)
        => configuration.GetConnectionString("ConsolidationDb")
            ?? throw new InvalidOperationException("ConnectionStrings:ConsolidationDb não configurada.");

    public static WebApplication ApplyMigrationsIfEnabled(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Configuration.GetValue("ApplyMigrationsOnStartup", false))
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ConsolidationDbContext>();
            db.Database.Migrate();
        }

        return app;
    }
}
