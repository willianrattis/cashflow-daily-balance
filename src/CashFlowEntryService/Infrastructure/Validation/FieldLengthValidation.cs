namespace CashFlowEntryService.Infrastructure.Validation;

/// <summary>
/// Request field lengths enforced at the API boundary, matching the database column sizes
/// (docs/04-schema-logico.md) — without this, an oversized value only fails at `SaveChangesAsync`,
/// surfacing as an unhandled `500` instead of a `400`.
/// </summary>
public static class FieldLengthValidation
{
    public const int DescriptionMaxLength = 1000;
    public const int IdempotencyKeyMaxLength = 200;

    public static void EnsureMaxLength(string? value, int maxLength, string fieldName)
    {
        if (value is not null && value.Length > maxLength)
        {
            throw new MalformedRequestException(
                "invalid_request",
                $"O campo '{fieldName}' excede o tamanho máximo de {maxLength} caracteres.",
                details: new { field = fieldName, maxLength });
        }
    }
}
