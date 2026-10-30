namespace CashFlowEntryService.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetCashFlowEntryDbConnectionString();

        services.AddDbContext<CashFlowEntryDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }

    public static string GetCashFlowEntryDbConnectionString(this IConfiguration configuration)
        => configuration.GetConnectionString("CashFlowEntryDb")
            ?? throw new InvalidOperationException("ConnectionStrings:CashFlowEntryDb não configurada.");

    public static WebApplication ApplyMigrationsIfEnabled(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Configuration.GetValue("ApplyMigrationsOnStartup", false))
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CashFlowEntryDbContext>();
            db.Database.Migrate();
        }

        return app;
    }
}
