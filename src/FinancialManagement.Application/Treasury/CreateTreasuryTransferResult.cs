using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.Treasury;

public sealed class CreateTreasuryTransferResult
{
    private CreateTreasuryTransferResult(
        bool success,
        CreateTreasuryTransferFailureReason failureReason,
        long? treasuryTransferId,
        string? reference,
        DateOnly? transferDate,
        TreasuryAccount? fromAccount,
        TreasuryAccount? toAccount,
        long amountMinor)
    {
        Success = success;
        FailureReason = failureReason;
        TreasuryTransferId = treasuryTransferId;
        Reference = reference;
        TransferDate = transferDate;
        FromAccount = fromAccount;
        ToAccount = toAccount;
        AmountMinor = amountMinor;
    }

    public bool Success { get; }

    public CreateTreasuryTransferFailureReason FailureReason { get; }

    public long? TreasuryTransferId { get; }

    public string? Reference { get; }

    public DateOnly? TransferDate { get; }

    public TreasuryAccount? FromAccount { get; }

    public TreasuryAccount? ToAccount { get; }

    public long AmountMinor { get; }

    public static CreateTreasuryTransferResult Failed(
        CreateTreasuryTransferFailureReason failureReason) =>
        new(false, failureReason, null, null, null, null, null, 0);

    public static CreateTreasuryTransferResult Succeeded(
        long id,
        string reference,
        DateOnly transferDate,
        TreasuryAccount fromAccount,
        TreasuryAccount toAccount,
        long amountMinor) =>
        new(true, CreateTreasuryTransferFailureReason.None, id, reference, transferDate,
            fromAccount, toAccount, amountMinor);
}
