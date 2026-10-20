namespace LaunchService.IntegrationTests.Infrastructure;

/// <summary>
/// Shared across the whole integration test collection: one real PostgreSQL container
/// (Testcontainers) and one in-process <see cref="WebApplicationFactory{TEntryPoint}"/>. No
/// RabbitMQ container is started here on purpose — these tests prove the write path works
/// without a broker (NFR-001); the Outbox Publisher simply fails to connect and retries, which
/// never blocks the HTTP response.
///
/// Overrides are applied as process environment variables rather than through
/// <c>ConfigureAppConfiguration</c>: Program.cs builds its own <c>WebApplicationBuilder</c> inside
/// top-level statements, and environment variables are the one configuration source guaranteed to
/// be read (and to win) regardless of exactly when/how WebApplicationFactory's host bootstrapping
/// hooks run for the minimal hosting model.
/// </summary>
public sealed class LaunchServiceTestFixture : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestIssuer = "cashflow-test-issuer";
    public const string TestAudience = "cashflow-test-api";
    public const string TestSigningKey = "cashflow-integration-test-signing-key-32-bytes-minimum";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17.6-alpine3.21")
        .WithDatabase("launch_db")
        .WithUsername("cashflow")
        .WithPassword("cashflow-test-only")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Development");
        Environment.SetEnvironmentVariable("ConnectionStrings__LaunchDb", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Jwt__Issuer", TestIssuer);
        Environment.SetEnvironmentVariable("Jwt__Audience", TestAudience);
        Environment.SetEnvironmentVariable("Jwt__SigningKey", TestSigningKey);
        Environment.SetEnvironmentVariable("RabbitMq__HostName", "127.0.0.1");
        Environment.SetEnvironmentVariable("RabbitMq__Port", "1"); // intentionally unreachable: no broker in these tests

        // Force WebApplicationFactory to build the host now that the environment is set, which
        // also runs the startup migration.
        _ = Server;
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await base.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
    }

    public LaunchDbContext CreateDbContext() => Services.CreateScope().ServiceProvider.GetRequiredService<LaunchDbContext>();
}
