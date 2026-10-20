namespace LaunchService.Infrastructure.Observability;

/// <summary>
/// Adds a Bearer security scheme to the generated OpenAPI document so Swagger UI shows an
/// "Authorize" button — without this, "Try it out" has no way to attach the dev JWT minted by
/// scripts/dev-jwt/make_token.py, since the policy-based auth is invisible to the OpenAPI
/// generator otherwise.
/// </summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            In = ParameterLocation.Header,
            Description = "Token de desenvolvimento: gere com scripts/dev-jwt/make_token.py (ver README.md). " +
                "Scopes: cashflow.launches.write, cashflow.launches.read, cashflow.consolidation.read."
        };

        var requirement = new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document, null)] = []
        };

        document.Security ??= [];
        document.Security.Add(requirement);

        return Task.CompletedTask;
    }
}
