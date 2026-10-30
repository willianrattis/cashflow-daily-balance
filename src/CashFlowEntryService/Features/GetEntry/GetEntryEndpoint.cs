namespace CashFlowEntryService.Features.GetEntry;

public static class GetEntryEndpoint
{
    public static RouteGroupBuilder MapGetEntry(this RouteGroupBuilder group)
    {
        group.MapGet("/entries/{entryId:guid}", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.EntriesRead)
            .RequireRateLimiting("reads")
            .WithName("GetEntry")
            .WithSummary("Consulta um lançamento imutável pelo seu identificador.")
            .Produces<EntryResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status400BadRequest, "application/json")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status404NotFound, "application/json")
            .Produces<ApiError>(StatusCodes.Status429TooManyRequests, "application/json");

        return group;
    }

    private static async Task<IResult> HandleAsync(
        Guid entryId,
        CashFlowEntryDbContext db,
        CancellationToken cancellationToken)
    {
        var entry = await db.Entries
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.EntryId == entryId, cancellationToken);

        if (entry is null)
        {
            throw new ResourceNotFoundException("entry_not_found", "Lançamento não encontrado.");
        }

        return Results.Ok(EntryResponse.From(entry));
    }
}
