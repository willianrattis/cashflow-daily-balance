namespace ConsolidationService.Infrastructure.Composition;

/// <summary>
/// What the HTTP contract (docs/03-contratos.md) looks like to a client: the OpenAPI document and
/// the guarantee that a bad request always reaches DomainExceptionHandler.
/// </summary>
public static class ApiContractExtensions
{
    public static IServiceCollection AddApiContract(this IServiceCollection services)
    {
        services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<BearerSecuritySchemeTransformer>();
            options.AddOperationTransformer<ContractHeadersOperationTransformer>();
        });

        // Minimal APIs only throw BadHttpRequestException for a malformed body or failed binding when
        // ThrowOnBadRequest is on, and the framework turns it on only in Development; elsewhere it
        // answers a bare 400 with an empty body and DomainExceptionHandler never runs. Forcing it on
        // keeps the `400 invalid_request` envelope (docs/03) identical in every environment.
        services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = true);

        return services;
    }

    /// <summary>
    /// Development only: `/openapi/v1.json` and the Swagger UI. Called right after
    /// <c>UseExceptionHandler</c>, so the Swagger UI middleware runs before the correlation,
    /// authentication and rate-limiting middleware.
    /// </summary>
    public static WebApplication MapDevelopmentApiDocs(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "ConsolidationService v1"));
        }

        return app;
    }
}
