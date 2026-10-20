namespace ConsolidationService.Infrastructure.Composition;

/// <summary>The service's own components: business clock, metrics, projector and the event consumer.</summary>
public static class ApplicationServicesExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        services.AddSingleton<IBusinessClock, BusinessClock>();
        services.AddSingleton<ConsolidationServiceMetrics>();
        services.AddScoped<ILaunchRecordedProjector, LaunchRecordedProjector>();
        services.AddHostedService<LaunchRecordedConsumerService>();

        return services;
    }
}
