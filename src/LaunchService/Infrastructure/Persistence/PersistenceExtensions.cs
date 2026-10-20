namespace LaunchService.Infrastructure.Persistence;

public static class PersistenceExtensions
{
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetLaunchDbConnectionString();

        services.AddDbContext<LaunchDbContext>(options => options.UseNpgsql(connectionString));

        return services;
    }

    public static string GetLaunchDbConnectionString(this IConfiguration configuration)
        => configuration.GetConnectionString("LaunchDb")
            ?? throw new InvalidOperationException("ConnectionStrings:LaunchDb não configurada.");

    public static WebApplication ApplyMigrationsIfEnabled(this WebApplication app)
    {
        if (app.Environment.IsDevelopment() || app.Configuration.GetValue("ApplyMigrationsOnStartup", false))
        {
            using var scope = app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<LaunchDbContext>();
            db.Database.Migrate();
        }

        return app;
    }
}
