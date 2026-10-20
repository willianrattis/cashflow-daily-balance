namespace LaunchService.Features.GetLaunch;

public static class GetLaunchEndpoint
{
    public static RouteGroupBuilder MapGetLaunch(this RouteGroupBuilder group)
    {
        group.MapGet("/launches/{launchId:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.LaunchesRead)
            .RequireRateLimiting("reads")
            .WithName("GetLaunch")
            .WithSummary("Consulta um lançamento imutável pelo seu identificador.")
            .Produces<LaunchResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status400BadRequest, "application/json")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status404NotFound, "application/json")
            .Produces<ApiError>(StatusCodes.Status429TooManyRequests, "application/json");

        return group;
    }

    private static async Task<IResult> HandleAsync(
        Guid launchId,
        LaunchDbContext db,
        CancellationToken cancellationToken)
    {
        var launch = await db.Launches
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.LaunchId == launchId, cancellationToken);

        if (launch is null)
        {
            throw new ResourceNotFoundException("launch_not_found", "Lançamento não encontrado.");
        }

        return Results.Ok(LaunchResponse.From(launch));
    }
}
