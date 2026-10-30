namespace ConsolidationService.IntegrationTests;

[Collection(ConsolidationServiceCollection.Name)]
public class GetConsolidationTests(ConsolidationServiceTestFixture fixture)
{
    private async Task SeedAsync(DateOnly businessDate, long amountInCents, string nature, DateTime? recordedAtUtc = null)
    {
        await TestEventPublisher.PublishCashFlowEntryRecordedAsync(
            fixture, Guid.NewGuid(), amountInCents, nature, businessDate, recordedAtUtc ?? DateTime.UtcNow);

        await Eventually.WaitUntilAsync(async () =>
        {
            await using var db = fixture.CreateDbContext();
            return await db.DailyConsolidations.AnyAsync(d => d.BusinessDate == businessDate);
        });
    }

    [Fact]
    public async Task Query_computes_opening_credits_debits_net_and_closing_for_the_window()
    {
        var blockStart = UniqueBusinessDate.NextBlockStart();
        var beforeWindow = blockStart;
        var insideWindowCredit = blockStart.AddDays(9);
        var insideWindowDebit = blockStart.AddDays(10);
        var fromDate = blockStart.AddDays(4);
        var toDate = blockStart.AddDays(15);

        await SeedAsync(beforeWindow, 10_000, "Credit"); // before the window
        await SeedAsync(insideWindowCredit, 5_000, "Credit"); // inside the window
        await SeedAsync(insideWindowDebit, 2_000, "Debit"); // inside the window

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        var response = await client.GetAsync($"/api/v1/consolidation?fromDate={fromDate:yyyy-MM-dd}&toDate={toDate:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(10_000, body.GetProperty("openingBalanceInCents").GetInt64());
        Assert.Equal(5_000, body.GetProperty("creditsInCents").GetInt64());
        Assert.Equal(2_000, body.GetProperty("debitsInCents").GetInt64());
        Assert.Equal(3_000, body.GetProperty("netChangeInCents").GetInt64());
        Assert.Equal(13_000, body.GetProperty("closingBalanceInCents").GetInt64());
    }

    [Fact]
    public async Task Query_returns_daily_breakdown_with_running_balance_across_a_gap_day()
    {
        var blockStart = UniqueBusinessDate.NextBlockStart();
        var beforeWindow = blockStart;
        var day1 = blockStart.AddDays(9);
        var day2 = day1.AddDays(1); // deliberately left with no movement
        var day3 = day1.AddDays(2);

        await SeedAsync(beforeWindow, 1_000, "Credit"); // before the window: interval opening
        await SeedAsync(day1, 5_000, "Credit");
        await SeedAsync(day3, 2_000, "Debit");

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        var response = await client.GetAsync($"/api/v1/consolidation?fromDate={day1:yyyy-MM-dd}&toDate={day3:yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var days = body.GetProperty("days").EnumerateArray().ToList();
        Assert.Equal(3, days.Count);

        Assert.Equal(day1.ToString("yyyy-MM-dd"), days[0].GetProperty("businessDate").GetString());
        Assert.Equal(1_000, days[0].GetProperty("openingBalanceInCents").GetInt64());
        Assert.Equal(5_000, days[0].GetProperty("creditsInCents").GetInt64());
        Assert.Equal(6_000, days[0].GetProperty("closingBalanceInCents").GetInt64());

        Assert.Equal(day2.ToString("yyyy-MM-dd"), days[1].GetProperty("businessDate").GetString());
        Assert.Equal(6_000, days[1].GetProperty("openingBalanceInCents").GetInt64()); // day1's closing
        Assert.Equal(0, days[1].GetProperty("creditsInCents").GetInt64());
        Assert.Equal(0, days[1].GetProperty("debitsInCents").GetInt64());
        Assert.Equal(6_000, days[1].GetProperty("closingBalanceInCents").GetInt64()); // no movement

        Assert.Equal(day3.ToString("yyyy-MM-dd"), days[2].GetProperty("businessDate").GetString());
        Assert.Equal(6_000, days[2].GetProperty("openingBalanceInCents").GetInt64()); // day2's closing
        Assert.Equal(2_000, days[2].GetProperty("debitsInCents").GetInt64());
        Assert.Equal(4_000, days[2].GetProperty("closingBalanceInCents").GetInt64());

        // The last day's closing must equal the interval's own closing (same running total).
        Assert.Equal(body.GetProperty("closingBalanceInCents").GetInt64(), days[^1].GetProperty("closingBalanceInCents").GetInt64());
    }

    [Fact]
    public async Task Query_with_a_date_range_longer_than_366_days_returns_422()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        var fromDate = new DateOnly(1900, 1, 1);
        var toDate = fromDate.AddDays(400); // comfortably over 366 days, still safely in the past

        var response = await client.GetAsync(
            $"/api/v1/consolidation?fromDate={fromDate:yyyy-MM-dd}&toDate={toDate:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("date_range_too_large", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Query_with_a_missing_fromDate_returns_400_with_the_standard_error_envelope()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        var response = await client.GetAsync("/api/v1/consolidation?toDate=2026-10-16");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // The standard envelope (docs/03-contratos.md), not ASP.NET Core's default ProblemDetails
        // shape ({type, title, status, ...}) — proves the framework's own binding-failure body
        // never reaches the caller.
        Assert.Equal("missing_required_parameter", body.GetProperty("code").GetString());
        Assert.True(body.TryGetProperty("message", out _));
        Assert.True(body.TryGetProperty("traceId", out _));
        Assert.False(body.TryGetProperty("type", out _), "a resposta não deveria usar o formato ProblemDetails padrão do framework.");
        Assert.False(body.TryGetProperty("title", out _), "a resposta não deveria usar o formato ProblemDetails padrão do framework.");
    }

    [Fact]
    public async Task Query_with_a_malformed_toDate_returns_400_with_the_standard_error_envelope()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        var response = await client.GetAsync("/api/v1/consolidation?fromDate=2026-10-01&toDate=not-a-date");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_date_format", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("type", out _), "a resposta não deveria usar o formato ProblemDetails padrão do framework.");
    }

    [Fact]
    public async Task Query_returns_as_of_and_lag_fields_after_one_event_with_a_plausible_lag()
    {
        var businessDate = UniqueBusinessDate.NextBlockStart();
        var recordedAtUtc = DateTime.UtcNow;

        await using var baselineDb = fixture.CreateDbContext();
        var baselineVersion = await baselineDb.ProjectionMetadata.Select(p => p.ProjectionVersion).FirstOrDefaultAsync();

        await SeedAsync(businessDate, 5_000, "Credit", recordedAtUtc);

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        var response = await client.GetAsync(
            $"/api/v1/consolidation?fromDate={businessDate:yyyy-MM-dd}&toDate={businessDate.AddDays(15):yyyy-MM-dd}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // NFR-005: `freshnessSeconds` is not part of the contract — a quiet period is not staleness.
        Assert.False(body.TryGetProperty("freshnessSeconds", out _), "freshnessSeconds não deveria existir na resposta.");

        // The projection has a single global version counter (one row for the whole system), so
        // this test's own event must have advanced it by exactly one, not reset it to 1.
        Assert.Equal(baselineVersion + 1, body.GetProperty("projectionVersion").GetInt64());

        var lastAppliedEventRecordedAtUtc = body.GetProperty("lastAppliedEventRecordedAtUtc").GetDateTime();
        Assert.True(
            Math.Abs((lastAppliedEventRecordedAtUtc - recordedAtUtc).TotalSeconds) < 1,
            $"lastAppliedEventRecordedAtUtc ({lastAppliedEventRecordedAtUtc:O}) deveria bater com o recordedAtUtc enviado ({recordedAtUtc:O}).");

        var lagSeconds = body.GetProperty("lastAppliedLagSeconds").GetDouble();
        Assert.InRange(lagSeconds, 0, 10);

        var asOfUtc = body.GetProperty("asOfUtc").GetDateTime();
        Assert.True(asOfUtc >= lastAppliedEventRecordedAtUtc, "asOfUtc deveria ser >= lastAppliedEventRecordedAtUtc.");
    }

    [Fact]
    public async Task Queried_10_seconds_after_the_last_event_the_lag_does_not_keep_growing()
    {
        var businessDate = UniqueBusinessDate.NextBlockStart();
        await SeedAsync(businessDate, 5_000, "Credit");

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));
        var url = $"/api/v1/consolidation?fromDate={businessDate:yyyy-MM-dd}&toDate={businessDate.AddDays(15):yyyy-MM-dd}";

        var firstBody = await (await client.GetAsync(url)).Content.ReadFromJsonAsync<JsonElement>();
        var firstLagSeconds = firstBody.GetProperty("lastAppliedLagSeconds").GetDouble();

        // No new activity anywhere during this wait — a quiet period is not stale (NFR-005). A
        // field computed as `now - lastEventOccurredAtUtc` would grow by ~10s here; this one must not.
        await Task.Delay(TimeSpan.FromSeconds(10));

        var secondBody = await (await client.GetAsync(url)).Content.ReadFromJsonAsync<JsonElement>();
        var secondLagSeconds = secondBody.GetProperty("lastAppliedLagSeconds").GetDouble();

        Assert.True(
            Math.Abs(secondLagSeconds - firstLagSeconds) < 1.0,
            $"O atraso não deveria crescer só porque o tempo passou sem novos eventos. " +
            $"Primeira consulta: {firstLagSeconds}s, segunda (10s depois): {secondLagSeconds}s.");
    }

    [Fact]
    public async Task Query_for_a_period_with_no_movement_returns_zeros_without_shifting_the_filter()
    {
        var blockStart = UniqueBusinessDate.NextBlockStart();
        await SeedAsync(blockStart, 1_000, "Credit");

        var fromDate = blockStart.AddDays(30);
        var toDate = blockStart.AddDays(31);

        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        var response = await client.GetAsync($"/api/v1/consolidation?fromDate={fromDate:yyyy-MM-dd}&toDate={toDate:yyyy-MM-dd}");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(fromDate.ToString("yyyy-MM-dd"), body.GetProperty("fromDate").GetString());
        Assert.Equal(toDate.ToString("yyyy-MM-dd"), body.GetProperty("toDate").GetString());
        Assert.Equal(0, body.GetProperty("creditsInCents").GetInt64());
        Assert.Equal(0, body.GetProperty("debitsInCents").GetInt64());
        Assert.Equal(1_000, body.GetProperty("openingBalanceInCents").GetInt64());
    }

    [Fact]
    public async Task Query_with_fromDate_after_toDate_returns_422()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        var response = await client.GetAsync("/api/v1/consolidation?fromDate=2026-10-16&toDate=2026-10-01");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Query_with_a_future_toDate_returns_422()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        var response = await client.GetAsync("/api/v1/consolidation?fromDate=2026-10-01&toDate=2099-01-01");

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task Correlation_id_longer_than_100_chars_returns_400_invalid_correlation_id()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/consolidation?fromDate=2026-10-01&toDate=2026-10-02");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", new string('f', 101));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_correlation_id", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Correlation_id_with_non_printable_characters_returns_400_invalid_correlation_id()
    {
        var client = fixture.CreateClient();
        client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/consolidation?fromDate=2026-10-01&toDate=2026-10-02");
        request.Headers.TryAddWithoutValidation("X-Correlation-Id", "abc\u0007def");

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("invalid_correlation_id", body.GetProperty("code").GetString());
    }
}
