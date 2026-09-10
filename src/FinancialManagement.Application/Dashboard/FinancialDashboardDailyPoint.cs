namespace FinancialManagement.Application.Dashboard;

public sealed record FinancialDashboardDailyPoint(
    DateOnly Date,
    long SalesMinor,
    long PurchasesMinor,
    long ExpensesMinor,
    long CustomerCollectionsMinor,
    long SupplierPaymentsMinor);
