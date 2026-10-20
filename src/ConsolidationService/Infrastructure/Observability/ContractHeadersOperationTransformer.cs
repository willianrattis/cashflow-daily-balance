namespace ConsolidationService.Infrastructure.Observability;

/// <summary>
/// Declares the header contract (docs/03-contratos.md) that Minimal API's OpenAPI generator
/// cannot infer from the route handler signature: `X-Correlation-Id` is read straight off
/// <c>HttpContext.Request.Headers</c>, never a bound parameter, on every operation. Also marks
/// `fromDate`/`toDate` as required on `GET /consolidation` — the handler binds them as nullable
/// strings on purpose (a missing/malformed value must surface the error envelope, not ASP.NET
/// Core's own binding-failure body; see GetConsolidationEndpoint's own comment), which otherwise
/// makes the generator describe them as optional query parameters.
/// </summary>
public sealed class ContractHeadersOperationTransformer : IOpenApiOperationTransformer
{
    public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        operation.Parameters ??= [];
        operation.Parameters.Add(new OpenApiParameter
        {
            Name = "X-Correlation-Id",
            In = ParameterLocation.Header,
            Required = false,
            Description = "Identificador de correlação fornecido pelo cliente; se ausente, o serviço gera um.",
            Schema = new OpenApiSchema { Type = JsonSchemaType.String }
        });

        if (context.Description.RelativePath == "api/v1/consolidation")
        {
            foreach (var parameter in operation.Parameters)
            {
                if (parameter is OpenApiParameter concreteParameter
                    && concreteParameter.In == ParameterLocation.Query
                    && concreteParameter.Name is "fromDate" or "toDate")
                {
                    concreteParameter.Required = true;
                }
            }
        }

        return Task.CompletedTask;
    }
}
