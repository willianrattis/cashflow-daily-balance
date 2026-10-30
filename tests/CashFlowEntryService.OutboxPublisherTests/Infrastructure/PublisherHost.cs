namespace CashFlowEntryService.OutboxPublisherTests.Infrastructure;

/// <summary>
/// One in-process CashFlowEntryService host — and so one real <c>OutboxPublisherService</c> — against
/// <see cref="OutboxPublisherTestFixture"/>'s containers, with its own <c>Outbox:PublishTimeoutMs</c>.
/// Created through <see cref="OutboxPublisherTestFixture.StartPublisher"/>.
///
/// The timeout is set per host through <c>PostConfigure&lt;OutboxOptions&gt;</c>, not through an
/// environment variable: options are bound lazily (first resolved by <c>ValidateOnStart</c> inside
/// <c>Build()</c>), so the post-configuration always applies, and two hosts in the same process
/// with different values never race on a process-wide variable.
/// </summary>
public sealed class PublisherHost(TimeSpan publishTimeout) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.ConfigureTestServices(services => services.PostConfigure<OutboxOptions>(
            options => options.PublishTimeoutMs = (int)publishTimeout.TotalMilliseconds));
    }
}
