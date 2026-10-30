namespace CashFlowEntryService.Infrastructure.Composition;

/// <summary>The service's own components: business clock, metrics, event publisher and the Outbox Publisher.</summary>
public static class ApplicationServicesExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IBusinessClock, BusinessClock>();
        services.AddSingleton<CashFlowEntryServiceMetrics>();
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        services.AddHostedService<OutboxPublisherService>();

        return services;
    }
}
