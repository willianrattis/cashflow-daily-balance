namespace LaunchService.Infrastructure.Composition;

/// <summary>
/// What the HTTP contract (docs/03-contratos.md) looks like to a client: the OpenAPI document,
/// strict JSON binding and the guarantee that a bad request always reaches DomainExceptionHandler.
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

        // Unknown JSON fields (e.g. a client-sent `businessDate`) must fail loudly instead of being
        // silently ignored — ASP.NET Core surfaces the rejection as BadHttpRequestException, mapped to
        // `400 invalid_request` by DomainExceptionHandler.
        services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow);

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
            app.UseSwaggerUI(options => options.SwaggerEndpoint("/openapi/v1.json", "LaunchService v1"));
        }

        return app;
    }
}
