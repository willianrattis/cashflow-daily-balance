namespace LaunchService.Infrastructure.Observability;

/// <summary>
/// Declares the header contract (docs/03-contratos.md) that Minimal API's OpenAPI generator
/// cannot infer from the route handler signature, because these headers are read straight off
/// <c>HttpContext.Request.Headers</c>/written to the response, never bound parameters:
/// `Idempotency-Key` (required on `POST /launches`), `X-Correlation-Id` (optional, every
/// operation) and the `Idempotency-Replayed` response header on a replay.
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

        if (context.Description.HttpMethod == HttpMethods.Post && context.Description.RelativePath == "api/v1/launches")
        {
            operation.Parameters.Add(new OpenApiParameter
            {
                Name = "Idempotency-Key",
                In = ParameterLocation.Header,
                Required = true,
                Description = "Chave de idempotência do comando; repetida com o mesmo payload retorna o mesmo lançamento.",
                Schema = new OpenApiSchema { Type = JsonSchemaType.String }
            });

            if (operation.Responses is not null
                && operation.Responses.TryGetValue("200", out var okResponse)
                && okResponse is OpenApiResponse concreteOkResponse)
            {
                concreteOkResponse.Headers ??= new Dictionary<string, IOpenApiHeader>();
                concreteOkResponse.Headers["Idempotency-Replayed"] = new OpenApiHeader
                {
                    Description = "Presente e igual a 'true' quando a resposta repete um lançamento já registrado.",
                    Schema = new OpenApiSchema { Type = JsonSchemaType.String }
                };
            }
        }

        return Task.CompletedTask;
    }
}
