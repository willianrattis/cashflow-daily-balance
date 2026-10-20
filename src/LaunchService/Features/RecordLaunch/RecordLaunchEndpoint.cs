namespace LaunchService.Features.RecordLaunch;

public static class RecordLaunchEndpoint
{
    public static RouteGroupBuilder MapRecordLaunch(this RouteGroupBuilder group)
    {
        group.MapPost("/launches", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.LaunchesWrite)
            .RequireRateLimiting("writes")
            .WithName("RecordLaunch")
            .WithSummary("Registra um lançamento de crédito ou débito.")
            .Produces<LaunchResponse>(StatusCodes.Status200OK)
            .Produces<LaunchResponse>(StatusCodes.Status201Created)
            .Produces<ApiError>(StatusCodes.Status400BadRequest, "application/json")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status409Conflict, "application/json")
            .Produces(StatusCodes.Status413PayloadTooLarge)
            .Produces<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/json")
            .Produces<ApiError>(StatusCodes.Status429TooManyRequests, "application/json");

        return group;
    }

    private static async Task<IResult> HandleAsync(
        RecordLaunchRequest request,
        HttpContext httpContext,
        LaunchDbContext db,
        IBusinessClock clock,
        LaunchServiceMetrics metrics,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await RecordAsync(request, httpContext, db, clock, cancellationToken);
            metrics.LaunchRequestsTotal.Add(
                1,
                new KeyValuePair<string, object?>("operation", "record"),
                new KeyValuePair<string, object?>("outcome", "success"));
            return result;
        }
        catch (Exception ex)
        {
            var outcome = ex switch
            {
                ValidationFailedException or MalformedRequestException => "validation",
                ConflictException => "conflict",
                _ => "error"
            };
            metrics.LaunchRequestsTotal.Add(
                1,
                new KeyValuePair<string, object?>("operation", "record"),
                new KeyValuePair<string, object?>("outcome", outcome));
            throw;
        }
        finally
        {
            metrics.LaunchRequestDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static async Task<IResult> RecordAsync(
        RecordLaunchRequest request,
        HttpContext httpContext,
        LaunchDbContext db,
        IBusinessClock clock,
        CancellationToken cancellationToken)
    {
        if (!httpContext.Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyKeyValues)
            || string.IsNullOrWhiteSpace(idempotencyKeyValues))
        {
            throw new MalformedRequestException("missing_idempotency_key", "O header Idempotency-Key é obrigatório.");
        }

        var idempotencyKey = idempotencyKeyValues.ToString();
        FieldLengthValidation.EnsureMaxLength(idempotencyKey, FieldLengthValidation.IdempotencyKeyMaxLength, "Idempotency-Key");
        FieldLengthValidation.EnsureMaxLength(request.Description, FieldLengthValidation.DescriptionMaxLength, "description");

        if (request.AmountInCents <= 0)
        {
            throw new ValidationFailedException("invalid_amount", "amountInCents deve ser positivo.");
        }

        if (!LaunchNatureExtensions.TryParse(request.Nature, out var nature))
        {
            throw new ValidationFailedException("invalid_nature", "nature deve ser 'Credit' ou 'Debit'.");
        }

        var fingerprint = LaunchFingerprint.Compute(request.AmountInCents, nature, request.Description);

        var existingByKey = await db.Launches
            .FirstOrDefaultAsync(l => l.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingByKey is not null)
        {
            return ResolveAgainstExisting(existingByKey, fingerprint, httpContext);
        }

        var principalId = httpContext.User.FindFirst("sub")?.Value ?? "unknown";
        var correlationId = CorrelationIdMiddleware.GetCorrelationId(httpContext);
        var businessDate = clock.TodayBusinessDate();
        var recordedAtUtc = clock.UtcNow();

        var launch = Launch.CreateNew(
            idempotencyKey,
            request.AmountInCents,
            nature,
            request.Description,
            businessDate,
            recordedAtUtc,
            principalId,
            correlationId);

        var outbox = OutboxMessage.FromLaunch(launch, recordedAtUtc);

        db.Launches.Add(launch);
        db.OutboxMessages.Add(outbox);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.TryGetUniqueViolationConstraint(out var constraintName))
        {
            db.ChangeTracker.Clear();
            return await ResolveRaceAsync(constraintName, idempotencyKey, fingerprint, db, httpContext, cancellationToken);
        }

        return Results.Created(
            $"/api/v1/launches/{launch.LaunchId}",
            LaunchResponse.From(launch));
    }

    private static IResult ResolveAgainstExisting(Launch existing, string fingerprint, HttpContext httpContext)
    {
        if (existing.RequestFingerprint == fingerprint)
        {
            httpContext.Response.Headers["Idempotency-Replayed"] = "true";
            return Results.Json(LaunchResponse.From(existing), statusCode: StatusCodes.Status200OK);
        }

        throw new ConflictException(
            "idempotency_payload_mismatch",
            "A chave já foi usada com um payload diferente.",
            details: new { launchId = existing.LaunchId });
    }

    private static async Task<IResult> ResolveRaceAsync(
        string constraintName,
        string idempotencyKey,
        string fingerprint,
        LaunchDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (constraintName == LaunchConfiguration.IdempotencyKeyIndexName)
        {
            var existing = await db.Launches.FirstAsync(
                l => l.IdempotencyKey == idempotencyKey, cancellationToken);
            return ResolveAgainstExisting(existing, fingerprint, httpContext);
        }

        throw new ConflictException("concurrent_write_conflict", "Conflito de concorrência não reconhecido ao registrar o lançamento.");
    }
}
