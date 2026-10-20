namespace ConsolidationService.Infrastructure.Errors;

/// <summary>
/// Base type for exceptions that carry a stable machine-readable <see cref="Code"/> and an HTTP
/// <see cref="StatusCode"/>, mapped to the error envelope documented in docs/03-contratos.md.
/// </summary>
public abstract class DomainException : Exception
{
    protected DomainException(string code, string message, int statusCode, object? details = null)
        : base(message)
    {
        Code = code;
        StatusCode = statusCode;
        Details = details;
    }

    public string Code { get; }
    public int StatusCode { get; }
    public object? Details { get; }
}

public sealed class ValidationFailedException(string code, string message, object? details = null)
    : DomainException(code, message, StatusCodes.Status422UnprocessableEntity, details);

/// <summary>Missing or malformed input the client must fix before retrying (e.g. an unparseable date).</summary>
public sealed class BadRequestException(string code, string message, object? details = null)
    : DomainException(code, message, StatusCodes.Status400BadRequest, details);

public sealed class ServiceUnavailableException(string code, string message, object? details = null)
    : DomainException(code, message, StatusCodes.Status503ServiceUnavailable, details);
