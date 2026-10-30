namespace CashFlowEntryService.Features.RecordEntry;

public sealed record RecordEntryRequest(
    long AmountInCents,
    string? Nature,
    string? Description = null);
