namespace ConsolidationService.IntegrationTests.Infrastructure;

/// <summary>
/// Shared across the whole integration test collection: a real PostgreSQL container and a real
/// RabbitMQ container (Testcontainers), plus one in-process
/// <see cref="WebApplicationFactory{TEntryPoint}"/> so the actual <c>CashFlowEntryRecordedConsumerService</c>
/// hosted service runs and consumes from the real broker — these tests publish events directly to
/// the shared exchange, exactly like CashFlowEntryService's Outbox Publisher would, and assert on the
/// resulting projection.
/// </summary>
public sealed class ConsolidationServiceTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestIssuer = "cashflow-test-issuer";
    public const string TestAudience = "cashflow-test-api";
    public const string TestSigningKey = "cashflow-integration-test-signing-key-32-bytes-minimum";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6-alpine3.21")
        .WithDatabase("consolidation_db")
        .WithUsername("cashflow")
        .WithPassword("cashflow-test-only")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:4.1.1-management-alpine")
        .WithUsername("cashflow")
        .WithPassword("cashflow-test-only")
        .Build();

    public string RabbitMqHostName => _rabbitMq.Hostname;
    public int RabbitMqPort => _rabbitMq.GetMappedPublicPort(5672);

    public async Task InitializeAsync()
    {
        await Task.WhenAll(_postgres.StartAsync(), _rabbitMq.StartAsync());

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ConnectionStrings__ConsolidationDb", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestAudience);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", TestSigningKey);
        Environment.SetEnvironmentVariable("RabbitMq__HostName", RabbitMqHostName);
        Environment.SetEnvironmentVariable("RabbitMq__Port", RabbitMqPort.ToString());
        Environment.SetEnvironmentVariable("RabbitMq__UserName", "cashflow");
        Environment.SetEnvironmentVariable("RabbitMq__Password", "cashflow-test-only");
        // Short retry ladder so the classifier/DLQ tests don't wait minutes.
        Environment.SetEnvironmentVariable("RabbitMq__RetryDelaysMs__0", "200");
        Environment.SetEnvironmentVariable("RabbitMq__RetryDelaysMs__1", "400");
        Environment.SetEnvironmentVariable("RabbitMq__RetryDelaysMs__2", "800");

        // Force WebApplicationFactory to build the host (and start the consumer hosted service).
        _ = Server;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await Task.WhenAll(_postgres.DisposeAsync().AsTask(), _rabbitMq.DisposeAsync().AsTask());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ProjectorFaultController>();
            services.AddScoped<ICashFlowEntryRecordedProjector>(sp => new FaultInjectingProjector(
                new CashFlowEntryRecordedProjector(sp.GetRequiredService<ConsolidationDbContext>()),
                sp.GetRequiredService<ProjectorFaultController>()));
        });
    }

    public ConsolidationDbContext CreateDbContext() => Services.CreateScope().ServiceProvider.GetRequiredService<ConsolidationDbContext>();

    public ProjectorFaultController Faults => Services.GetRequiredService<ProjectorFaultController>();

    /// <summary>
    /// Freezes the real Postgres container (cgroup freezer, like `docker pause`) — simulates
    /// "Consolidation DB unreachable" without tearing down its network/port mapping, the same
    /// pattern <c>OutboxPublisherTestFixture.PauseRabbitMqAsync</c> uses for the broker and for
    /// the same reason: a restarted Testcontainers container gets a new ephemeral host port,
    /// which would leave the already-bound `ConnectionStrings:ConsolidationDb` pointed at a
    /// stale, permanently-dead port instead of a merely-unresponsive one.
    /// </summary>
    public Task PauseDatabaseAsync() => _postgres.PauseAsync();

    /// <summary>Unfreezes the database container previously paused via <see cref="PauseDatabaseAsync"/>.</summary>
    public Task UnpauseDatabaseAsync() => _postgres.UnpauseAsync();
}
