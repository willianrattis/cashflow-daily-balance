namespace ConsolidationService.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    /// <summary>
    /// Delay, in milliseconds, of each retry "parking" level (CashFlowTopology). The queue names
    /// (`...retry.1|2|3`) are position-based, not duration-based, so changing this does not make
    /// queue names lie. At least one value is required (validated in
    /// <see cref="CashFlowTopology.BuildRetryLevels"/>); the production/dev default lives in
    /// appsettings.json, not here — the configuration binder appends config array children onto
    /// whatever array a property already holds instead of overwriting it by index, so a non-empty
    /// default here would silently grow past 3 levels whenever any provider (e.g. an env var
    /// override in tests) also supplies this key.
    /// </summary>
    public int[] RetryDelaysMs { get; set; } = [];
}
