namespace FinancialManagement.Application.Dashboard;

public sealed class FinancialDashboardResult
{
    private FinancialDashboardResult(
        bool success,
        FinancialDashboardFailureReason failureReason,
        DateOnly? filterStartDate,
        DateOnly? filterEndDate,
        long totalSalesMinor,
        long totalPurchasesMinor,
        long customerCollectionsMinor,
        long supplierPaymentsMinor,
        long expensesMinor,
        long resultMinor,
        long currentCustomerReceivablesMinor,
        long currentSupplierPayablesMinor,
        long currentCashBalanceMinor,
        long currentBankBalanceMinor,
        long currentTotalTreasuryMinor,
        bool treasuryIsInitialized,
        DateOnly? treasuryOpeningDate)
    {
        Success = success;
        FailureReason = failureReason;
        FilterStartDate = filterStartDate;
        FilterEndDate = filterEndDate;
        TotalSalesMinor = totalSalesMinor;
        TotalPurchasesMinor = totalPurchasesMinor;
        CustomerCollectionsMinor = customerCollectionsMinor;
        SupplierPaymentsMinor = supplierPaymentsMinor;
        ExpensesMinor = expensesMinor;
        ResultMinor = resultMinor;
        CurrentCustomerReceivablesMinor = currentCustomerReceivablesMinor;
        CurrentSupplierPayablesMinor = currentSupplierPayablesMinor;
        CurrentCashBalanceMinor = currentCashBalanceMinor;
        CurrentBankBalanceMinor = currentBankBalanceMinor;
        CurrentTotalTreasuryMinor = currentTotalTreasuryMinor;
        TreasuryIsInitialized = treasuryIsInitialized;
        TreasuryOpeningDate = treasuryOpeningDate;
    }

    public bool Success { get; }

    public FinancialDashboardFailureReason FailureReason { get; }

    public DateOnly? FilterStartDate { get; }

    public DateOnly? FilterEndDate { get; }

    public long TotalSalesMinor { get; }

    public long TotalPurchasesMinor { get; }

    public long CustomerCollectionsMinor { get; }

    public long SupplierPaymentsMinor { get; }

    public long ExpensesMinor { get; }

    public long ResultMinor { get; }

    public long CurrentCustomerReceivablesMinor { get; }

    public long CurrentSupplierPayablesMinor { get; }

    public long CurrentCashBalanceMinor { get; }

    public long CurrentBankBalanceMinor { get; }

    public long CurrentTotalTreasuryMinor { get; }

    public bool TreasuryIsInitialized { get; }

    public DateOnly? TreasuryOpeningDate { get; }

    public static FinancialDashboardResult Failed(
        FinancialDashboardQuery query,
        FinancialDashboardFailureReason failureReason) =>
        new(
            success: false,
            failureReason,
            query.StartDate,
            query.EndDate,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            0,
            false,
            null);

    public static FinancialDashboardResult Succeeded(
        FinancialDashboardQuery query,
        long totalSalesMinor,
        long totalPurchasesMinor,
        long customerCollectionsMinor,
        long supplierPaymentsMinor,
        long expensesMinor,
        long resultMinor,
        long currentCustomerReceivablesMinor,
        long currentSupplierPayablesMinor,
        long currentCashBalanceMinor,
        long currentBankBalanceMinor,
        long currentTotalTreasuryMinor,
        bool treasuryIsInitialized,
        DateOnly? treasuryOpeningDate) =>
        new(
            success: true,
            FinancialDashboardFailureReason.None,
            query.StartDate,
            query.EndDate,
            totalSalesMinor,
            totalPurchasesMinor,
            customerCollectionsMinor,
            supplierPaymentsMinor,
            expensesMinor,
            resultMinor,
            currentCustomerReceivablesMinor,
            currentSupplierPayablesMinor,
            currentCashBalanceMinor,
            currentBankBalanceMinor,
            currentTotalTreasuryMinor,
            treasuryIsInitialized,
            treasuryOpeningDate);
}
