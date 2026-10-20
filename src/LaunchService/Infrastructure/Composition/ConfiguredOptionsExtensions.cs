namespace LaunchService.Infrastructure.Composition;

/// <summary>Binds every configuration section the service reads through <c>IOptions</c>.</summary>
public static class ConfiguredOptionsExtensions
{
    public static IServiceCollection AddConfiguredOptions(this IServiceCollection services, IConfiguration configuration)
    {
        // ValidateOnStart (not just Validate) makes an invalid value fail the host at startup, in
        // `app.Build()`/`app.Run()` — before the process ever reports healthy or accepts a request —
        // instead of surfacing as a confusing hot loop or silent misbehavior the first time the option
        // is actually used (e.g. `Outbox:BatchSize=0` would otherwise spin OutboxPublisherService's
        // cycle with an empty claim every time, never sleeping).
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .Validate(o => !string.IsNullOrWhiteSpace(o.HostName), "RabbitMq:HostName não pode ser vazio.")
            .Validate(o => o.Port is > 0 and <= 65535, "RabbitMq:Port deve estar entre 1 e 65535.")
            .ValidateOnStart();

        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .Validate(o => o.BatchSize > 0, "Outbox:BatchSize deve ser positivo.")
            .Validate(o => o.PollIntervalMs > 0, "Outbox:PollIntervalMs deve ser positivo.")
            .Validate(o => o.PublishTimeoutMs > 0, "Outbox:PublishTimeoutMs deve ser positivo.")
            .ValidateOnStart();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        services.AddOptions<RateLimitOptions>()
            .Bind(configuration.GetSection(RateLimitOptions.SectionName))
            .Validate(o => o.WriteRequestsPerSecond > 0, "RateLimiting:WriteRequestsPerSecond deve ser positivo.")
            .Validate(o => o.ReadRequestsPerSecond > 0, "RateLimiting:ReadRequestsPerSecond deve ser positivo.")
            .ValidateOnStart();

        return services;
    }
}
