namespace ConsolidationService.Infrastructure.Composition;

/// <summary>Binds every configuration section the service reads through <c>IOptions</c>.</summary>
public static class ConfiguredOptionsExtensions
{
    public static IServiceCollection AddConfiguredOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart (not just Validate) makes an invalid value fail the host at startup, in
        // `app.Build()`/`app.Run()` — before the process ever reports healthy or consumes a message —
        // instead of surfacing only when LaunchRecordedConsumerService first builds the retry ladder
        // (`CashFlowTopology.BuildRetryLevels`, which already throws for an empty list, but not until the
        // hosted service starts, and never for a configured delay that is zero/negative).
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.HostName), "RabbitMq:HostName não pode ser vazio.")
            .Validate(o => o.Port is > 0 and <= 65535, "RabbitMq:Port deve estar entre 1 e 65535.")
            .Validate(o => o.RetryDelaysMs.Length > 0, "RabbitMq:RetryDelaysMs deve ter ao menos um valor configurado.")
            .Validate(o => Array.TrueForAll(o.RetryDelaysMs, delay => delay > 0), "RabbitMq:RetryDelaysMs só aceita valores positivos.")
            .ValidateOnStart();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .Validate(o => o.ReadRequestsPerSecond > 0, "RateLimiting:ReadRequestsPerSecond deve ser positivo.")
            .ValidateOnStart();

        return services;
    }
}
