namespace ConsolidationService.Infrastructure.Observability;

/// <summary>
/// Accepts `X-Correlation-Id` from the caller, generates one when absent, propagates it on the
/// response, and attaches it to the logging scope for every request (docs/05-aspectos-transversais.md).
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger, IHostEnvironment environment)
{
    public const string HeaderName = "X-Correlation-Id";
    public const int MaxLength = 100;

    public async Task InvokeAsync(HttpContext context)
    {
        string correlationId;
        if (context.Request.Headers.TryGetValue(HeaderName, out var existing) && !string.IsNullOrWhiteSpace(existing))
        {
            var candidate = existing.ToString();
            if (candidate.Length > MaxLength || candidate.Any(char.IsControl))
            {
                throw new BadRequestException(
                    "invalid_correlation_id",
                    $"X-Correlation-Id deve ter no máximo {MaxLength} caracteres imprimíveis.");
            }

            correlationId = candidate;
        }
        else
        {
            correlationId = Guid.NewGuid().ToString("N");
        }

        context.Items[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object?>
        {
            ["service"] = "ConsolidationService",
            ["environment"] = environment.EnvironmentName,
            ["correlationId"] = correlationId,
            ["traceId"] = TraceIdAccessor.GetTraceId(context)
        }))
        {
            await next(context);
        }
    }

    public static string GetCorrelationId(HttpContext context)
        => context.Items.TryGetValue(HeaderName, out var value) && value is string correlationId
            ? correlationId
            : context.TraceIdentifier;
}
