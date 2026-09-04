namespace FinancialManagement.Application.Treasury;

public sealed class TreasurySummaryResult
{
    private TreasurySummaryResult(
        bool success,
        TreasurySummaryFailureReason failureReason,
        bool isInitialized,
        DateOnly? openingDate,
        long cashOpeningMinor,
        long bankOpeningMinor,
        long customerCashInflowsMinor,
        long customerBankInflowsMinor,
        long supplierCashOutflowsMinor,
        long supplierBankOutflowsMinor,
        long expenseCashOutflowsMinor,
        long expenseBankOutflowsMinor,
        long cashToBankTransfersMinor,
        long bankToCashTransfersMinor,
        long cashBalanceMinor,
        long bankBalanceMinor,
        long totalTreasuryMinor)
    {
        Success = success;
        FailureReason = failureReason;
        IsInitialized = isInitialized;
        OpeningDate = openingDate;
        CashOpeningMinor = cashOpeningMinor;
        BankOpeningMinor = bankOpeningMinor;
        CustomerCashInflowsMinor = customerCashInflowsMinor;
        CustomerBankInflowsMinor = customerBankInflowsMinor;
        SupplierCashOutflowsMinor = supplierCashOutflowsMinor;
        SupplierBankOutflowsMinor = supplierBankOutflowsMinor;
        ExpenseCashOutflowsMinor = expenseCashOutflowsMinor;
        ExpenseBankOutflowsMinor = expenseBankOutflowsMinor;
        CashToBankTransfersMinor = cashToBankTransfersMinor;
        BankToCashTransfersMinor = bankToCashTransfersMinor;
        CashBalanceMinor = cashBalanceMinor;
        BankBalanceMinor = bankBalanceMinor;
        TotalTreasuryMinor = totalTreasuryMinor;
    }

    public bool Success { get; }

    public TreasurySummaryFailureReason FailureReason { get; }

    public bool IsInitialized { get; }

    public DateOnly? OpeningDate { get; }

    public long CashOpeningMinor { get; }

    public long BankOpeningMinor { get; }

    public long CustomerCashInflowsMinor { get; }

    public long CustomerBankInflowsMinor { get; }

    public long SupplierCashOutflowsMinor { get; }

    public long SupplierBankOutflowsMinor { get; }

    public long ExpenseCashOutflowsMinor { get; }

    public long ExpenseBankOutflowsMinor { get; }

    public long CashToBankTransfersMinor { get; }

    public long BankToCashTransfersMinor { get; }

    public long CashBalanceMinor { get; }

    public long BankBalanceMinor { get; }

    public long TotalTreasuryMinor { get; }

    public static TreasurySummaryResult Failed(TreasurySummaryFailureReason failureReason) =>
        new(false, failureReason, false, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static TreasurySummaryResult Succeeded(
        bool isInitialized,
        DateOnly? openingDate,
        long cashOpeningMinor,
        long bankOpeningMinor,
        long customerCashInflowsMinor,
        long customerBankInflowsMinor,
        long supplierCashOutflowsMinor,
        long supplierBankOutflowsMinor,
        long expenseCashOutflowsMinor,
        long expenseBankOutflowsMinor,
        long cashToBankTransfersMinor,
        long bankToCashTransfersMinor,
        long cashBalanceMinor,
        long bankBalanceMinor,
        long totalTreasuryMinor) =>
        new(
            true,
            TreasurySummaryFailureReason.None,
            isInitialized,
            openingDate,
            cashOpeningMinor,
            bankOpeningMinor,
            customerCashInflowsMinor,
            customerBankInflowsMinor,
            supplierCashOutflowsMinor,
            supplierBankOutflowsMinor,
            expenseCashOutflowsMinor,
            expenseBankOutflowsMinor,
            cashToBankTransfersMinor,
            bankToCashTransfersMinor,
            cashBalanceMinor,
            bankBalanceMinor,
            totalTreasuryMinor);
}
