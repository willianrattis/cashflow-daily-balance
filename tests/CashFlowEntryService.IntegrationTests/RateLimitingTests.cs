namespace CashFlowEntryService.IntegrationTests;

/// <summary>
/// Fixed-window rate limiting partitioned per principal (`sub` claim, DoS control, docs/05 §1).
/// Needs a tiny `RateLimiting:WriteRequestsPerSecond` to stay fast and
/// deterministic instead of the production default (100/s) — this Program.cs reads
/// configuration during its own top-level statements, so (like
/// <see cref="CashFlowEntryServiceTestFixture"/>'s own comment on why it uses environment variables
/// instead of <c>ConfigureAppConfiguration</c>/<c>UseSetting</c>) the only override that is
/// reliably honored is a process environment variable read by a freshly built second host —
/// never the shared <paramref name="fixture"/> host, which was already built in
/// <c>InitializeAsync</c>. Set/unset around each second host, like
/// <c>OutboxThroughputTests</c>'s second-host tests; collection tests run sequentially, so
/// nothing else observes the temporary value.
/// </summary>
[Collection(CashFlowEntryServiceCollection.Name)]
#pragma warning disable CS9113 // unused, but required so xUnit initializes the shared fixture (containers, env vars) before this collection's tests run
public class RateLimitingTests(CashFlowEntryServiceTestFixture fixture)
#pragma warning restore CS9113
{
    private static HttpRequestMessage BuildRequest(string idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/entries")
        {
            Content = JsonContent.Create(new
            {
                amountInCents = 1000,
                nature = "Credit"
            })
        };
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    [Fact]
    public async Task Exceeding_the_write_limit_returns_429_with_the_standard_envelope()
    {
        Environment.SetEnvironmentVariable("RateLimiting__WriteRequestsPerSecond", "2");
        WebApplicationFactory<Program> limitedFactory = new();
        try
        {
            var client = limitedFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.entries.write"));

            var statusCodes = new List<HttpStatusCode>();
            for (var i = 0; i < 6; i++)
            {
                var response = await client.SendAsync(BuildRequest($"key-ratelimit-{i}"));
                statusCodes.Add(response.StatusCode);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var body = await response.Content.ReadFromJsonAsync<JsonElement>();
                    Assert.Equal("rate_limit_exceeded", body.GetProperty("code").GetString());
                    Assert.True(body.TryGetProperty("traceId", out _));
                    return;
                }
            }

            Assert.Fail($"Esperava pelo menos uma resposta 429 dentro de 6 requisições; obteve: {string.Join(", ", statusCodes)}");
        }
        finally
        {
            limitedFactory.Dispose();
            Environment.SetEnvironmentVariable("RateLimiting__WriteRequestsPerSecond", null);
        }
    }

    [Fact]
    public async Task A_429_is_counted_as_rate_limited_and_logged_with_the_correlation_id()
    {
        Environment.SetEnvironmentVariable("RateLimiting__WriteRequestsPerSecond", "2");
        var logs = new CapturingLoggerProvider();
        WebApplicationFactory<Program> baseFactory = new();
        var limitedFactory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureServices(services => services.AddSingleton<ILoggerProvider>(logs)));
        try
        {
            var client = limitedFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.entries.write"));

            var rejectedCorrelationIds = new List<string>();
            for (var i = 0; i < 10; i++)
            {
                var request = BuildRequest($"key-ratelimit-metric-{i}");
                request.Headers.Add("X-Correlation-Id", $"burst-{i}");
                var response = await client.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    rejectedCorrelationIds.Add($"burst-{i}");
                }
            }

            Assert.NotEmpty(rejectedCorrelationIds);

            var scrape = await client.GetStringAsync("/metrics");
            Assert.Equal(rejectedCorrelationIds.Count, ReadCounter(scrape, "cashflow_entry_requests_total", "operation=\"record\"", "outcome=\"rate_limited\""));

            var warnings = logs.Entries
                .Where(entry => entry.Category == typeof(RateLimitRejection).FullName && entry.Level == LogLevel.Warning)
                .ToList();
            Assert.Equal(rejectedCorrelationIds, warnings.Select(entry => entry.State["CorrelationId"] as string));
        }
        finally
        {
            limitedFactory.Dispose();
            baseFactory.Dispose();
            Environment.SetEnvironmentVariable("RateLimiting__WriteRequestsPerSecond", null);
        }
    }

    /// <summary>Sums the Prometheus text-format samples of <paramref name="metric"/> carrying every given label.</summary>
    private static long ReadCounter(string scrape, string metric, params string[] labels) =>
        scrape.Split('\n')
            .Where(line => line.StartsWith(metric + "{", StringComparison.Ordinal) && labels.All(line.Contains))
            .Sum(line => (long)double.Parse(line[(line.IndexOf('}') + 1)..].Trim().Split(' ')[0], System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public async Task Different_principals_are_rate_limited_independently()
    {
        Environment.SetEnvironmentVariable("RateLimiting__WriteRequestsPerSecond", "2");
        WebApplicationFactory<Program> limitedFactory = new();
        try
        {
            var subjectAClient = limitedFactory.CreateClient();
            subjectAClient.DefaultRequestHeaders.Authorization = new(
                "Bearer", TestTokenFactory.Create(scope: "cashflow.entries.write", subject: "subject-a"));

            HttpStatusCode? subjectAThrottledStatus = null;
            for (var i = 0; i < 6; i++)
            {
                var response = await subjectAClient.SendAsync(BuildRequest($"key-partition-a-{i}"));
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    subjectAThrottledStatus = response.StatusCode;
                    break;
                }
            }
            Assert.Equal(HttpStatusCode.TooManyRequests, subjectAThrottledStatus);

            var subjectBClient = limitedFactory.CreateClient();
            subjectBClient.DefaultRequestHeaders.Authorization = new(
                "Bearer", TestTokenFactory.Create(scope: "cashflow.entries.write", subject: "subject-b"));

            var subjectBResponse = await subjectBClient.SendAsync(BuildRequest("key-partition-b-0"));
            Assert.Equal(HttpStatusCode.Created, subjectBResponse.StatusCode);
        }
        finally
        {
            limitedFactory.Dispose();
            Environment.SetEnvironmentVariable("RateLimiting__WriteRequestsPerSecond", null);
        }
    }

    [Fact]
    public async Task Health_endpoint_is_never_rate_limited()
    {
        Environment.SetEnvironmentVariable("RateLimiting__ReadRequestsPerSecond", "1");
        WebApplicationFactory<Program> limitedFactory = new();
        try
        {
            var client = limitedFactory.CreateClient();

            for (var i = 0; i < 10; i++)
            {
                var response = await client.GetAsync("/health/live");
                Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            }
        }
        finally
        {
            limitedFactory.Dispose();
            Environment.SetEnvironmentVariable("RateLimiting__ReadRequestsPerSecond", null);
        }
    }
}
