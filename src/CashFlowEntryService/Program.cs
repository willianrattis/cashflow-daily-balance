var builder = WebApplication.CreateBuilder(args);

builder.AddStructuredLogging();
builder.Services.AddApiContract();              // OpenAPI, strict JSON, ThrowOnBadRequest
builder.AddRequestLimits();                     // Kestrel body size
builder.Services.AddPersistence(builder.Configuration);
builder.Services.AddConfiguredOptions(builder.Configuration);
var rateLimitOptions = builder.Configuration.GetRateLimitOptions();
builder.AddJwtAuthentication();                 // signing-key guard, AuthN, AuthZ policies
builder.Services.AddPrincipalRateLimiting(rateLimitOptions);
builder.Services.AddApplicationServices();      // clock, metrics, publisher, Outbox Publisher
builder.Services.AddMetricsExporter();          // OpenTelemetry → Prometheus
builder.Services.AddErrorHandling();            // DomainExceptionHandler, ProblemDetails
builder.Services.AddDependencyHealthChecks(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.MapDevelopmentApiDocs();                    // OpenAPI + Swagger UI, Development only
app.UseRequestPipeline();                       // correlation → AuthN → AuthZ → rate limiter
app.MapOperationalEndpoints();                  // health checks, /metrics
app.MapApiEndpoints();                          // /api/v1
app.ApplyMigrationsIfEnabled();

app.Run();

public partial class Program;
