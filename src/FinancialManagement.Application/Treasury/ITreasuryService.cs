namespace FinancialManagement.Application.Treasury;

public interface ITreasuryService
{
    Task<CreateTreasuryOpeningBalanceResult> CreateOpeningBalanceAsync(
        CreateTreasuryOpeningBalanceCommand command,
        CancellationToken cancellationToken = default);

    Task<CreateTreasuryTransferResult> CreateTransferAsync(
        CreateTreasuryTransferCommand command,
        CancellationToken cancellationToken = default);

    Task<TreasurySummaryResult> GetSummaryAsync(
        CancellationToken cancellationToken = default);
}
