namespace CashFlowEntryService.OutboxPublisherTests.Infrastructure;

/// <summary>
/// Shared across this project's single collection: a real PostgreSQL container and a real
/// RabbitMQ container (Testcontainers). Each test starts its own in-process CashFlowEntryService host
/// (<see cref="StartPublisher"/>), so the actual <c>OutboxPublisherService</c> hosted service runs
/// against a real broker with the <c>Outbox:PublishTimeoutMs</c> that test verifies — tests in a
/// collection run one at a time, so only the current test's publisher is ever claiming rows.
///
/// This lives in its own test project — not inside <c>CashFlowEntryService.IntegrationTests</c> — on
/// purpose: that project's fixture already sets process-wide `RabbitMq__*`
/// environment variables pointing at an intentionally unreachable port to prove NFR-001 without
/// a broker. A second fixture in the same assembly needing a real broker address would race on
/// those same process-wide variables whenever xUnit runs collections from that assembly in
/// parallel. `dotnet test` runs each test project as its own process, so a separate project
/// avoids the race entirely instead of relying on configuration injection that this codebase's
/// minimal-hosting `Program.cs` does not reliably honor (see CashFlowEntryServiceTestFixture's own
/// comment on why it uses environment variables instead of ConfigureAppConfiguration/UseSetting).
/// The one per-test setting, the publish timeout, is an option rather than configuration read
/// in <c>Program.cs</c>, so <see cref="PublisherHost"/> sets it per host without a variable.
///
/// Deliberately does NOT declare/bind the consumer's queue — that stays ConsolidationService's
/// job (ADR-003). Tests that need the queue bound do so themselves, mid-test, through a
/// throwaway admin channel, to simulate ConsolidationService starting late.
/// </summary>
public sealed class OutboxPublisherTestFixture : IAsyncLifetime
{
    /// <summary>
    /// Publish timeout for every test that does not verify the timeout itself. Measured with the
    /// whole solution's tests running in parallel (`dotnet test CashFlow.sln`), the broker's
    /// answer to the first publish took about 7s — 9ms with this project alone — so even the
    /// production default (5000ms) times out. A publish that times out is retried, and because
    /// the broker had already accepted it the retry is a second copy in the queue — an expected
    /// at-least-once duplicate (ADR-003), but one that breaks "exactly once" and "first failure
    /// is unroutable" assertions. 30s leaves a 4x margin over that delay for a slower CI runner
    /// and still fits inside the tests' 60s waits.
    /// </summary>
    public static readonly TimeSpan LoadSafePublishTimeout = TimeSpan.FromSeconds(30);

    public const string TestIssuer = "cashflow-test-issuer";
    public const string TestAudience = "cashflow-test-api";
    public const string TestSigningKey = "cashflow-integration-test-signing-key-32-bytes-minimum";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6-alpine3.21")
        .WithDatabase("cashflow_entry_db")
        .WithUsername("cashflow")
        .WithPassword("cashflow-test-only")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.1.1-management-alpine")
        .WithUsername("cashflow")
        .WithPassword("cashflow-test-only")
        .Build();

    // Deliberately the literal loopback address, not `_rabbitMq.Hostname` ("localhost"): on a
    // dual-stack dev machine "localhost" resolves to both ::1 and 127.0.0.1, and RabbitMQ.Client
    // tries each resolved address in turn, each bounded by its own RequestedConnectionTimeout —
    // against a paused (frozen, not refusing) broker that roughly doubles the real connect bound
    // instead of enforcing the single timeout the short-circuit tests rely on.
    public string RabbitMqHostName => "127.0.0.1";
    public int RabbitMqPort => _rabbitMq.GetMappedPublicPort(5672);
    public const string RabbitMqUserName = "cashflow";
    public const string RabbitMqPassword = "cashflow-test-only";

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ConnectionStrings__CashFlowEntryDb", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestAudience);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", TestSigningKey);
        Environment.SetEnvironmentVariable("RabbitMq__HostName", RabbitMqHostName);
        Environment.SetEnvironmentVariable("RabbitMq__Port", RabbitMqPort.ToString());
        Environment.SetEnvironmentVariable("RabbitMq__UserName", RabbitMqUserName);
        Environment.SetEnvironmentVariable("RabbitMq__Password", RabbitMqPassword);
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask());
    }

    /// <summary>
    /// Builds and starts a CashFlowEntryService host (startup migrations included, so the schema exists
    /// once this returns) whose Outbox Publisher uses <paramref name="publishTimeout"/>. The
    /// caller disposes it, which stops that publisher.
    /// </summary>
    public PublisherHost StartPublisher(TimeSpan publishTimeout)
    {
        var host = new PublisherHost(publishTimeout);
        _ = host.Server;
        return host;
    }

    /// <summary>A context on the fixture's database, independent of any running host.</summary>
    public CashFlowEntryDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<CashFlowEntryDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    /// <summary>
    /// Freezes the real broker container (cgroup freezer, like `docker pause`) — simulates "broker
    /// unreachable" for the short-circuit tests without tearing down its network/port
    /// mapping. Deliberately NOT <c>StopAsync</c>/<c>StartAsync</c>: Testcontainers assigns a new
    /// ephemeral host port on a restarted container, which would leave the already-running
    /// <see cref="RabbitMqOptions"/> (bound once from env vars in <see cref="InitializeAsync"/>)
    /// pointed at a stale, permanently-dead port — the publisher would never recover even after
    /// the broker is back up. Pausing keeps the same port for the container's whole lifetime.
    /// </summary>
    public Task PauseRabbitMqAsync() => _rabbitMq.PauseAsync();

    /// <summary>Unfreezes the broker container previously paused via <see cref="PauseRabbitMqAsync"/>.</summary>
    public Task UnpauseRabbitMqAsync() => _rabbitMq.UnpauseAsync();
}
