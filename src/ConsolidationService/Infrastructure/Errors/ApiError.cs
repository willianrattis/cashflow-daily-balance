namespace ConsolidationService.Infrastructure.Errors;

/// <summary>Error envelope shape required by docs/03-contratos.md.</summary>
public sealed record ApiError(string Code, string Message, string TraceId, object? Details = null);
