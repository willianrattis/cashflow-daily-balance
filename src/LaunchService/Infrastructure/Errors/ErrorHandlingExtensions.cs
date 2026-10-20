namespace LaunchService.Infrastructure.Errors;

public static class ErrorHandlingExtensions
{
    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        services.AddExceptionHandler<DomainExceptionHandler>();
        services.AddProblemDetails();

        return services;
    }
}
