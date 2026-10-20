namespace ConsolidationService.Infrastructure.Errors;

/// <summary>
/// Maps <see cref="DomainException"/> (and otherwise-unhandled exceptions) to the error envelope
/// {code, message, traceId, details} instead of the default ASP.NET Core ProblemDetails shape.
/// </summary>
public sealed class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is DomainException domainException)
        {
            httpContext.Response.StatusCode = domainException.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(
                new ApiError(domainException.Code, domainException.Message, TraceIdAccessor.GetTraceId(httpContext), domainException.Details),
                cancellationToken);
            return true;
        }

        // Malformed JSON, binding errors and unmapped-field bodies all surface as
        // BadHttpRequestException regardless of environment; map them to the standard envelope
        // instead of letting the framework's default ProblemDetails (or a bare 500) through.
        // Kestrel rejects oversized bodies (413) before this pipeline runs, so that status
        // never reaches this handler.
        if (exception is BadHttpRequestException badHttpRequestException)
        {
            httpContext.Response.StatusCode = badHttpRequestException.StatusCode;
            await httpContext.Response.WriteAsJsonAsync(
                new ApiError("invalid_request", badHttpRequestException.Message, TraceIdAccessor.GetTraceId(httpContext)),
                cancellationToken);
            return true;
        }

        logger.LogError(exception, "Erro não tratado processando {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(
            new ApiError("internal_error", "Ocorreu um erro inesperado.", TraceIdAccessor.GetTraceId(httpContext)),
            cancellationToken);
        return true;
    }
}
