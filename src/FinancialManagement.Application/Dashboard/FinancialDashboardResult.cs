namespace FinancialManagement.Application.Dashboard;

public sealed class FinancialDashboardResult
{
    private FinancialDashboardResult(
        bool success,
        FinancialDashboardFailureReason failureReason,
        DateOnly? filterStartDate,
        DateOnly? filterEndDate,
        DateOnly? comparisonStartDate,
        DateOnly? comparisonEndDate,
        long totalSalesMinor,
        long totalPurchasesMinor,
        long customerCollectionsMinor,
        long supplierPaymentsMinor,
        long expensesMinor,
        long resultMinor,
        long comparisonSalesMinor,
        long comparisonPurchasesMinor,
        long comparisonExpensesMinor,
        long comparisonResultMinor,
        long comparisonCustomerCollectionsMinor,
        long comparisonSupplierPaymentsMinor,
        long currentCustomerReceivablesMinor,
        long currentSupplierPayablesMinor,
        long currentCashBalanceMinor,
        long currentBankBalanceMinor,
        long currentTotalTreasuryMinor,
        bool treasuryIsInitialized,
        DateOnly? treasuryOpeningDate,
        IReadOnlyList<FinancialDashboardDailyPoint> dailyPoints,
        IReadOnlyList<FinancialDashboardPartyBalance> topCustomerReceivables,
        IReadOnlyList<FinancialDashboardPartyBalance> topSupplierPayables,
        int customerReceivablePartyCount,
        int supplierPayablePartyCount)
    {
        Success = success;
        FailureReason = failureReason;
        FilterStartDate = filterStartDate;
        FilterEndDate = filterEndDate;
        ComparisonStartDate = comparisonStartDate;
        ComparisonEndDate = comparisonEndDate;
        TotalSalesMinor = totalSalesMinor;
        TotalPurchasesMinor = totalPurchasesMinor;
        CustomerCollectionsMinor = customerCollectionsMinor;
        SupplierPaymentsMinor = supplierPaymentsMinor;
        ExpensesMinor = expensesMinor;
        ResultMinor = resultMinor;
        ComparisonSalesMinor = comparisonSalesMinor;
        ComparisonPurchasesMinor = comparisonPurchasesMinor;
        ComparisonExpensesMinor = comparisonExpensesMinor;
        ComparisonResultMinor = comparisonResultMinor;
        ComparisonCustomerCollectionsMinor = comparisonCustomerCollectionsMinor;
        ComparisonSupplierPaymentsMinor = comparisonSupplierPaymentsMinor;
        CurrentCustomerReceivablesMinor = currentCustomerReceivablesMinor;
        CurrentSupplierPayablesMinor = currentSupplierPayablesMinor;
        CurrentCashBalanceMinor = currentCashBalanceMinor;
        CurrentBankBalanceMinor = currentBankBalanceMinor;
        CurrentTotalTreasuryMinor = currentTotalTreasuryMinor;
        TreasuryIsInitialized = treasuryIsInitialized;
        TreasuryOpeningDate = treasuryOpeningDate;
        DailyPoints = dailyPoints;
        TopCustomerReceivables = topCustomerReceivables;
        TopSupplierPayables = topSupplierPayables;
        CustomerReceivablePartyCount = customerReceivablePartyCount;
        SupplierPayablePartyCount = supplierPayablePartyCount;
    }

    public bool Success { get; }

    public FinancialDashboardFailureReason FailureReason { get; }

    public DateOnly? FilterStartDate { get; }

    public DateOnly? FilterEndDate { get; }

    public DateOnly? ComparisonStartDate { get; }

    public DateOnly? ComparisonEndDate { get; }

    public bool HasComparison => ComparisonStartDate.HasValue && ComparisonEndDate.HasValue;

    public FinancialDashboardGranularity Granularity { get; private init; }

    public long TotalSalesMinor { get; }

    public long TotalPurchasesMinor { get; }

    public long CustomerCollectionsMinor { get; }

    public long SupplierPaymentsMinor { get; }

    public long ExpensesMinor { get; }

    public long ResultMinor { get; }

    public long ComparisonSalesMinor { get; }

    public long ComparisonPurchasesMinor { get; }

    public long ComparisonExpensesMinor { get; }

    public long ComparisonResultMinor { get; }

    public long ComparisonCustomerCollectionsMinor { get; }

    public long ComparisonSupplierPaymentsMinor { get; }

    public long CurrentCustomerReceivablesMinor { get; }

    public long CurrentSupplierPayablesMinor { get; }

    public long CurrentCashBalanceMinor { get; }

    public long CurrentBankBalanceMinor { get; }

    public long CurrentTotalTreasuryMinor { get; }

    public bool TreasuryIsInitialized { get; }

    public DateOnly? TreasuryOpeningDate { get; }

    public IReadOnlyList<FinancialDashboardDailyPoint> DailyPoints { get; }

    public IReadOnlyList<FinancialDashboardPartyBalance> TopCustomerReceivables { get; }

    public IReadOnlyList<FinancialDashboardPartyBalance> TopSupplierPayables { get; }

    public int CustomerReceivablePartyCount { get; }

    public int SupplierPayablePartyCount { get; }

    public static FinancialDashboardResult Failed(
        FinancialDashboardQuery query,
        FinancialDashboardFailureReason failureReason) =>
        new(
            success: false,
            failureReason: failureReason,
            filterStartDate: query.StartDate,
            filterEndDate: query.EndDate,
            comparisonStartDate: query.ComparisonStartDate,
            comparisonEndDate: query.ComparisonEndDate,
            totalSalesMinor: 0,
            totalPurchasesMinor: 0,
            customerCollectionsMinor: 0,
            supplierPaymentsMinor: 0,
            expensesMinor: 0,
            resultMinor: 0,
            comparisonSalesMinor: 0,
            comparisonPurchasesMinor: 0,
            comparisonExpensesMinor: 0,
            comparisonResultMinor: 0,
            comparisonCustomerCollectionsMinor: 0,
            comparisonSupplierPaymentsMinor: 0,
            currentCustomerReceivablesMinor: 0,
            currentSupplierPayablesMinor: 0,
            currentCashBalanceMinor: 0,
            currentBankBalanceMinor: 0,
            currentTotalTreasuryMinor: 0,
            treasuryIsInitialized: false,
            treasuryOpeningDate: null,
            dailyPoints: Array.Empty<FinancialDashboardDailyPoint>(),
            topCustomerReceivables: Array.Empty<FinancialDashboardPartyBalance>(),
            topSupplierPayables: Array.Empty<FinancialDashboardPartyBalance>(),
            customerReceivablePartyCount: 0,
            supplierPayablePartyCount: 0)
        {
            Granularity = query.Granularity
        };

    public static FinancialDashboardResult Succeeded(
        FinancialDashboardQuery query,
        long totalSalesMinor,
        long totalPurchasesMinor,
        long customerCollectionsMinor,
        long supplierPaymentsMinor,
        long expensesMinor,
        long resultMinor,
        long comparisonSalesMinor,
        long comparisonPurchasesMinor,
        long comparisonExpensesMinor,
        long comparisonResultMinor,
        long comparisonCustomerCollectionsMinor,
        long comparisonSupplierPaymentsMinor,
        long currentCustomerReceivablesMinor,
        long currentSupplierPayablesMinor,
        long currentCashBalanceMinor,
        long currentBankBalanceMinor,
        long currentTotalTreasuryMinor,
        bool treasuryIsInitialized,
        DateOnly? treasuryOpeningDate,
        IReadOnlyList<FinancialDashboardDailyPoint> dailyPoints,
        IReadOnlyList<FinancialDashboardPartyBalance> topCustomerReceivables,
        IReadOnlyList<FinancialDashboardPartyBalance> topSupplierPayables,
        int customerReceivablePartyCount,
        int supplierPayablePartyCount) =>
        new(
            success: true,
            FinancialDashboardFailureReason.None,
            query.StartDate,
            query.EndDate,
            query.ComparisonStartDate,
            query.ComparisonEndDate,
            totalSalesMinor,
            totalPurchasesMinor,
            customerCollectionsMinor,
            supplierPaymentsMinor,
            expensesMinor,
            resultMinor,
            comparisonSalesMinor,
            comparisonPurchasesMinor,
            comparisonExpensesMinor,
            comparisonResultMinor,
            comparisonCustomerCollectionsMinor,
            comparisonSupplierPaymentsMinor,
            currentCustomerReceivablesMinor,
            currentSupplierPayablesMinor,
            currentCashBalanceMinor,
            currentBankBalanceMinor,
            currentTotalTreasuryMinor,
            treasuryIsInitialized,
            treasuryOpeningDate,
            dailyPoints,
            topCustomerReceivables,
            topSupplierPayables,
            customerReceivablePartyCount,
            supplierPayablePartyCount)
        {
            Granularity = query.Granularity
        };
}
