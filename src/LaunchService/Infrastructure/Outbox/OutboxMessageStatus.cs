namespace LaunchService.Infrastructure.Outbox;

public enum OutboxMessageStatus
{
    Pending,
    Published,
    Failed
}
