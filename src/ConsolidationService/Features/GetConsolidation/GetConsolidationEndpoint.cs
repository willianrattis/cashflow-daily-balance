namespace ConsolidationService.Features.GetConsolidation;

public static class GetConsolidationEndpoint
{
    /// <summary>
    /// A year's worth of days plus a little slack for leap years, not a hard calendar-year rule —
    /// the point is bounding how much work (and how many `days[]` entries) a single request can
    /// demand, not any particular reporting period.
    /// </summary>
    private const int MaxDateRangeDays = 366;

    public static RouteGroupBuilder MapGetConsolidation(this RouteGroupBuilder group)
    {
        group.MapGet("/consolidation", HandleAsync)
            .RequireAuthorization(AuthorizationPolicies.ConsolidationRead)
            .RequireRateLimiting("reads")
            .WithName("GetConsolidation")
            .WithSummary("Consulta o consolidado diário por intervalo inclusivo de datas.")
            .Produces<ConsolidationResponse>(StatusCodes.Status200OK)
            .Produces<ApiError>(StatusCodes.Status400BadRequest, "application/json")
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<ApiError>(StatusCodes.Status422UnprocessableEntity, "application/json")
            .Produces<ApiError>(StatusCodes.Status429TooManyRequests, "application/json")
            .Produces<ApiError>(StatusCodes.Status503ServiceUnavailable, "application/json");

        return group;
    }

    private static async Task<IResult> HandleAsync(
        string? fromDate,
        string? toDate,
        ConsolidationDbContext db,
        IBusinessClock clock,
        ConsolidationServiceMetrics metrics,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            // Parsed by hand instead of binding straight to DateOnly: a missing or malformed
            // query parameter would otherwise short-circuit before this handler runs, with
            // ASP.NET Core's own binding-failure body instead of our error envelope (BR-02).
            var parsedFromDate = ParseRequiredDate(fromDate, "fromDate");
            var parsedToDate = ParseRequiredDate(toDate, "toDate");

            if (parsedFromDate > parsedToDate)
            {
                throw new ValidationFailedException("invalid_date_range", "fromDate não pode ser posterior a toDate.");
            }

            var today = clock.TodayBusinessDate();
            if (parsedToDate > today)
            {
                throw new ValidationFailedException("future_date_not_allowed", "toDate não pode ser posterior à data atual de América/São Paulo.");
            }

            if (parsedToDate.DayNumber - parsedFromDate.DayNumber + 1 > MaxDateRangeDays)
            {
                throw new ValidationFailedException(
                    "date_range_too_large", $"O intervalo entre fromDate e toDate não pode exceder {MaxDateRangeDays} dias.");
            }

            ConsolidationResponse response;
            try
            {
                response = await QueryAsync(parsedFromDate, parsedToDate, db, cancellationToken);
            }
            catch (Exception ex) when (IsDatabaseUnavailable(ex))
            {
                throw new ServiceUnavailableException(
                    "consolidation_unavailable", "A projeção do Consolidado está temporariamente indisponível.");
            }

            metrics.ConsolidationRequestsTotal.Add(1, new KeyValuePair<string, object?>("outcome", "success"));
            return Results.Ok(response);
        }
        catch (Exception ex)
        {
            var outcome = ex switch
            {
                BadRequestException or ValidationFailedException => "validation",
                ServiceUnavailableException => "unavailable",
                _ => "error"
            };
            metrics.ConsolidationRequestsTotal.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
            throw;
        }
        finally
        {
            metrics.ConsolidationQueryDurationMs.Record(stopwatch.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// EF Core's own `NpgsqlExecutionStrategy` wraps a transient Npgsql failure (connection
    /// dropped, timeout reading from the stream, ...) in a generic
    /// <see cref="InvalidOperationException"/> ("An exception has been raised that is likely due
    /// to a transient failure") before it reaches this handler — so the real
    /// <see cref="NpgsqlException"/>/<see cref="TimeoutException"/> is only visible one level
    /// down, as <see cref="Exception.InnerException"/>, not as the exception's own type.
    /// </summary>
    private static bool IsDatabaseUnavailable(Exception exception)
    {
        for (var ex = exception; ex is not null; ex = ex.InnerException)
        {
            if (ex is NpgsqlException or TimeoutException)
            {
                return true;
            }
        }

        return false;
    }

    private static DateOnly ParseRequiredDate(string? rawValue, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            throw new BadRequestException(
                "missing_required_parameter", $"O parâmetro '{parameterName}' é obrigatório.", new { parameter = parameterName });
        }

        if (!DateOnly.TryParse(rawValue, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            throw new BadRequestException(
                "invalid_date_format", $"O parâmetro '{parameterName}' não é uma data válida (esperado AAAA-MM-DD).", new { parameter = parameterName, value = rawValue });
        }

        return parsed;
    }

    private static async Task<ConsolidationResponse> QueryAsync(
        DateOnly fromDate, DateOnly toDate, ConsolidationDbContext db, CancellationToken ct)
    {
        // REPEATABLE READ: every read below sees the same snapshot, so a projector commit that
        // lands mid-request can never split the opening balance and the window's own totals
        // across two different versions of the data (read skew, BR-02).
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        var openingBalance = await db.DailyConsolidations
            .Where(d => d.BusinessDate < fromDate)
            .SumAsync(d => (long?)d.NetChangeInCents, ct) ?? 0;

        var windowRows = await db.DailyConsolidations
            .Where(d => d.BusinessDate >= fromDate && d.BusinessDate <= toDate)
            .Select(d => new { d.BusinessDate, d.CreditsInCents, d.DebitsInCents })
            .ToListAsync(ct);

        var credits = windowRows.Sum(r => r.CreditsInCents);
        var debits = windowRows.Sum(r => r.DebitsInCents);

        var firstEntryDate = await db.DailyConsolidations
            .OrderBy(d => d.BusinessDate)
            .Select(d => (DateOnly?)d.BusinessDate)
            .FirstOrDefaultAsync(ct);

        var metadata = await db.ProjectionMetadata.FirstOrDefaultAsync(ct);

        await transaction.CommitAsync(ct);

        var totals = ConsolidationCalculator.Calculate(openingBalance, credits, debits);

        var perDayTotals = windowRows.ToDictionary(r => r.BusinessDate, r => (r.CreditsInCents, r.DebitsInCents));
        var days = ConsolidationCalculator.CalculateDailyBreakdown(fromDate, toDate, openingBalance, perDayTotals)
            .Select(d => new DailyBreakdownResponse(
                d.BusinessDate, d.OpeningBalanceInCents, d.CreditsInCents, d.DebitsInCents, d.NetChangeInCents, d.ClosingBalanceInCents))
            .ToList();

        // asOfUtc = when the projection was last updated, not "now": a quiet period with no
        // recent entries is not stale (NFR-005). Falls back to now only when there is no
        // metadata yet (no applied event) — the empty projection is, trivially, current as of
        // right now.
        var asOfUtc = metadata?.LastAppliedAtUtc ?? DateTime.UtcNow;
        var lastAppliedEventRecordedAtUtc = metadata?.LastAppliedEventRecordedAtUtc;
        var lastAppliedLagSeconds = metadata is null
            ? (double?)null
            : Math.Max(0, (metadata.LastAppliedAtUtc - metadata.LastAppliedEventRecordedAtUtc).TotalSeconds);

        return new ConsolidationResponse(
            FromDate: fromDate,
            ToDate: toDate,
            FirstEntryDate: firstEntryDate,
            OpeningBalanceInCents: totals.OpeningBalanceInCents,
            CreditsInCents: totals.CreditsInCents,
            DebitsInCents: totals.DebitsInCents,
            NetChangeInCents: totals.NetChangeInCents,
            ClosingBalanceInCents: totals.ClosingBalanceInCents,
            Days: days,
            AsOfUtc: asOfUtc,
            LastAppliedEventRecordedAtUtc: lastAppliedEventRecordedAtUtc,
            LastAppliedLagSeconds: lastAppliedLagSeconds,
            ProjectionVersion: metadata?.ProjectionVersion ?? 0);
    }
}
