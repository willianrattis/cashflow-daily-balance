namespace CashFlowEntryService.Features.RecordEntry;

public static class RecordEntryEndpoint
{
    public static RouteGroupBuilder MapRecordEntry(this RouteGroupBuilder group)
    {
        group.MapPost("/entries", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.EntriesWrite)
            .RequireRateLimiting("writes")
            .WithName("RecordEntry")
            .WithSummary("Registra um lançamento de crédito ou débito.")
            .Produces<EntryResponse>(StatusCodes.Status200OK)
            .Produces<EntryResponse>(StatusCodes.Status201Created)
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
        RecordEntryRequest request,
        HttpContext httpContext,
        CashFlowEntryDbContext db,
        IBusinessClock clock,
        CashFlowEntryServiceMetrics metrics,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = await RecordAsync(request, httpContext, db, clock, cancellationToken);
            metrics.EntryRequestsTotal.Add(
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
            metrics.EntryRequestsTotal.Add(
                1,
                new KeyValuePair<string, object?>("operation", "record"),
                new KeyValuePair<string, object?>("outcome", outcome));
            throw;
        }
        finally
        {
            metrics.EntryRequestDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    private static async Task<IResult> RecordAsync(
        RecordEntryRequest request,
        HttpContext httpContext,
        CashFlowEntryDbContext db,
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

        if (!EntryNatureExtensions.TryParse(request.Nature, out var nature))
        {
            throw new ValidationFailedException("invalid_nature", "nature deve ser 'Credit' ou 'Debit'.");
        }

        var fingerprint = EntryFingerprint.Compute(request.AmountInCents, nature, request.Description);

        var existingByKey = await db.Entries
            .FirstOrDefaultAsync(l => l.IdempotencyKey == idempotencyKey, cancellationToken);
        if (existingByKey is not null)
        {
            return ResolveAgainstExisting(existingByKey, fingerprint, httpContext);
        }

        var principalId = httpContext.User.FindFirst("sub")?.Value ?? "unknown";
        var correlationId = CorrelationIdMiddleware.GetCorrelationId(httpContext);
        var businessDate = clock.TodayBusinessDate();
        var recordedAtUtc = clock.UtcNow();

        var entry = CashFlowEntry.CreateNew(
            idempotencyKey,
            request.AmountInCents,
            nature,
            request.Description,
            businessDate,
            recordedAtUtc,
            principalId,
            correlationId);

        var outbox = OutboxMessage.FromEntry(entry, recordedAtUtc);

        db.Entries.Add(entry);
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
            $"/api/v1/entries/{entry.EntryId}",
            EntryResponse.From(entry));
    }

    private static IResult ResolveAgainstExisting(CashFlowEntry existing, string fingerprint, HttpContext httpContext)
    {
        if (existing.RequestFingerprint == fingerprint)
        {
            httpContext.Response.Headers["Idempotency-Replayed"] = "true";
            return Results.Json(EntryResponse.From(existing), statusCode: StatusCodes.Status200OK);
        }

        throw new ConflictException(
            "idempotency_payload_mismatch",
            "A chave já foi usada com um payload diferente.",
            details: new { entryId = existing.EntryId });
    }

    private static async Task<IResult> ResolveRaceAsync(
        string constraintName,
        string idempotencyKey,
        string fingerprint,
        CashFlowEntryDbContext db,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (constraintName == CashFlowEntryConfiguration.IdempotencyKeyIndexName)
        {
            var existing = await db.Entries.FirstAsync(
                l => l.IdempotencyKey == idempotencyKey, cancellationToken);
            return ResolveAgainstExisting(existing, fingerprint, httpContext);
        }

        throw new ConflictException("concurrent_write_conflict", "Conflito de concorrência não reconhecido ao registrar o lançamento.");
    }
}
