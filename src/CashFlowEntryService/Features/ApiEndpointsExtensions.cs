namespace CashFlowEntryService.Features;

public static class ApiEndpointsExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app)
    {
        var apiGroup = app.MapGroup("/api/v1");
        apiGroup.MapRecordEntry();
        apiGroup.MapGetEntry();

        return app;
    }
}
