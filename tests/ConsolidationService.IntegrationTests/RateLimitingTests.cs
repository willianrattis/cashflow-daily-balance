namespace ConsolidationService.IntegrationTests;

/// <summary>
/// A `429` from the read limiter (docs/05 §1) must be observable: counted as
/// <c>consolidation_requests_total{outcome="rate_limited"}</c> and logged once as a Warning with
/// the correlation id (ADR-010). Uses a second host with a tiny
/// `RateLimiting:ReadRequestsPerSecond`, set as a process environment variable for the same reason
/// <see cref="JwtSigningKeyGuardTests"/> does; collection tests run sequentially. The second host's
/// consumer is removed so it never competes with the shared fixture's consumer for messages.
/// </summary>
[Collection(ConsolidationServiceCollection.Name)]
#pragma warning disable CS9113 // unused, but required so xUnit initializes the shared fixture (containers, env vars) before this collection's tests run
public class RateLimitingTests(ConsolidationServiceTestFixture fixture)
#pragma warning restore CS9113
{
    [Fact]
    public async Task A_429_is_counted_as_rate_limited_and_logged_with_the_correlation_id()
    {
        Environment.SetEnvironmentVariable("RateLimiting__ReadRequestsPerSecond", "2");
        var logs = new CapturingLoggerProvider();
        WebApplicationFactory<Program> baseFactory = new();
        var limitedFactory = baseFactory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
            {
                services.AddSingleton<ILoggerProvider>(logs);
                var consumer = services.Single(descriptor =>
                    descriptor.ServiceType == typeof(IHostedService)
                    && descriptor.ImplementationType == typeof(LaunchRecordedConsumerService));
                services.Remove(consumer);
            }));
        try
        {
            var client = limitedFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new("Bearer", TestTokenFactory.Create(scope: "cashflow.consolidation.read"));

            var rejectedCorrelationIds = new List<string>();
            for (var i = 0; i < 10; i++)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/consolidation?fromDate=2020-01-01&toDate=2020-01-02");
                request.Headers.Add("X-Correlation-Id", $"burst-{i}");
                var response = await client.SendAsync(request);
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    rejectedCorrelationIds.Add($"burst-{i}");
                }
            }

            Assert.NotEmpty(rejectedCorrelationIds);

            var scrape = await client.GetStringAsync("/metrics");
            Assert.Equal(rejectedCorrelationIds.Count, ReadCounter(scrape, "consolidation_requests_total", "outcome=\"rate_limited\""));

            var warnings = logs.Entries
                .Where(entry => entry.Category == typeof(RateLimitRejection).FullName && entry.Level == LogLevel.Warning)
                .ToList();
            Assert.Equal(rejectedCorrelationIds, warnings.Select(entry => entry.State["CorrelationId"] as string));
        }
        finally
        {
            limitedFactory.Dispose();
            baseFactory.Dispose();
            Environment.SetEnvironmentVariable("RateLimiting__ReadRequestsPerSecond", null);
        }
    }

    /// <summary>Sums the Prometheus text-format samples of <paramref name="metric"/> carrying every given label.</summary>
    private static long ReadCounter(string scrape, string metric, params string[] labels) =>
        scrape.Split('\n')
            .Where(line => line.StartsWith(metric + "{", StringComparison.Ordinal) && labels.All(line.Contains))
            .Sum(line => (long)double.Parse(line[(line.IndexOf('}') + 1)..].Trim().Split(' ')[0], CultureInfo.InvariantCulture));
}
