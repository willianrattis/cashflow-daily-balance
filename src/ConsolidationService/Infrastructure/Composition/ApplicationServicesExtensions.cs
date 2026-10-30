namespace ConsolidationService.Infrastructure.Composition;

/// <summary>The service's own components: business clock, metrics, projector and the event consumer.</summary>
public static class ApplicationServicesExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IBusinessClock, BusinessClock>();
        services.AddSingleton<ConsolidationServiceMetrics>();
        services.AddScoped<ICashFlowEntryRecordedProjector, CashFlowEntryRecordedProjector>();
        services.AddHostedService<CashFlowEntryRecordedConsumerService>();

        return services;
    }
}
