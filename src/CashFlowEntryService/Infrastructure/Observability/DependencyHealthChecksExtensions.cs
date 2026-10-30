namespace CashFlowEntryService.Infrastructure.Observability;

public static class DependencyHealthChecksExtensions
{
    public static IServiceCollection AddDependencyHealthChecks(this IServiceCollection services, IConfiguration configuration)
    {
        // Singleton so BrokerHealthCheck's 10s result cache actually persists across calls — AddCheck<T>
        // below defaults to a transient registration (a fresh, cache-less instance per check) unless the
        // type is already registered, which this line ensures.
        services.AddSingleton<BrokerHealthCheck>();
        services.AddHealthChecks()
            .AddCheck("entry-db", new DatabaseHealthCheck(configuration.GetCashFlowEntryDbConnectionString()), tags: ["ready"])
            .AddCheck<BrokerHealthCheck>("rabbitmq", tags: ["broker"]);

        return services;
    }
}
