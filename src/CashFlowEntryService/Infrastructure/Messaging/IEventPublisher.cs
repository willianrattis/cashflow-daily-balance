namespace CashFlowEntryService.Infrastructure.Messaging;

public interface IEventPublisher
{
    /// <summary>
    /// Publishes a single event using the broker's <c>mandatory</c> flag with publisher
    /// confirmations enabled. Must throw — never return successfully — when the broker nacks the
    /// message or cannot route it (e.g. no queue is bound to the routing key yet). The caller
    /// (Outbox Publisher) treats any exception as a retryable failure and never marks the Outbox
    /// row Published on failure (an unroutable message must stay pending, not be silently
    /// dropped).
    /// </summary>
    Task PublishAsync(Guid eventId, string routingKey, string jsonPayload, string? correlationId, CancellationToken cancellationToken);
}
