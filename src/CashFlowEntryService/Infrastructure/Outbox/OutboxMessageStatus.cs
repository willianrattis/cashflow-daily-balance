namespace CashFlowEntryService.Infrastructure.Outbox;

public enum OutboxMessageStatus
{
    Pending,
    Published,
    Failed
}
