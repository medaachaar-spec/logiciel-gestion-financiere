namespace FinancialManagement.Application.Treasury;

public sealed class CreateTreasuryOpeningBalanceResult
{
    private CreateTreasuryOpeningBalanceResult(
        bool success,
        CreateTreasuryOpeningBalanceFailureReason failureReason,
        long? treasuryOpeningBalanceId,
        DateOnly? openingDate,
        long cashAmountMinor,
        long bankAmountMinor)
    {
        Success = success;
        FailureReason = failureReason;
        TreasuryOpeningBalanceId = treasuryOpeningBalanceId;
        OpeningDate = openingDate;
        CashAmountMinor = cashAmountMinor;
        BankAmountMinor = bankAmountMinor;
    }

    public bool Success { get; }

    public CreateTreasuryOpeningBalanceFailureReason FailureReason { get; }

    public long? TreasuryOpeningBalanceId { get; }

    public DateOnly? OpeningDate { get; }

    public long CashAmountMinor { get; }

    public long BankAmountMinor { get; }

    public static CreateTreasuryOpeningBalanceResult Failed(
        CreateTreasuryOpeningBalanceFailureReason failureReason) =>
        new(false, failureReason, null, null, 0, 0);

    public static CreateTreasuryOpeningBalanceResult Succeeded(
        long id,
        DateOnly openingDate,
        long cashAmountMinor,
        long bankAmountMinor) =>
        new(true, CreateTreasuryOpeningBalanceFailureReason.None, id, openingDate,
            cashAmountMinor, bankAmountMinor);
}
