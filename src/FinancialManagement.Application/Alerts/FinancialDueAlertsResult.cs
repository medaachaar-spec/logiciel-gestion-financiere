namespace FinancialManagement.Application.Alerts;

public sealed class FinancialDueAlertsResult
{
    private FinancialDueAlertsResult(
        bool success,
        FinancialDueAlertsFailureReason failureReason,
        DateOnly? currentDate,
        IReadOnlyList<FinancialDueAlert> customerAlerts,
        IReadOnlyList<FinancialDueAlert> supplierAlerts)
    {
        Success = success;
        FailureReason = failureReason;
        CurrentDate = currentDate;
        CustomerAlerts = customerAlerts;
        SupplierAlerts = supplierAlerts;
    }

    public bool Success { get; }

    public FinancialDueAlertsFailureReason FailureReason { get; }

    public DateOnly? CurrentDate { get; }

    public IReadOnlyList<FinancialDueAlert> CustomerAlerts { get; }

    public IReadOnlyList<FinancialDueAlert> SupplierAlerts { get; }

    public int TotalCustomerAlertCount => CustomerAlerts.Count;

    public int TotalSupplierAlertCount => SupplierAlerts.Count;

    public int OverdueCustomerCount => CustomerAlerts.Count(alert =>
        alert.Status == FinancialDueAlertStatus.Overdue);

    public int OverdueSupplierCount => SupplierAlerts.Count(alert =>
        alert.Status == FinancialDueAlertStatus.Overdue);

    public static FinancialDueAlertsResult Failed(
        FinancialDueAlertsFailureReason failureReason) =>
        new(false, failureReason, null, Array.Empty<FinancialDueAlert>(), Array.Empty<FinancialDueAlert>());

    public static FinancialDueAlertsResult Succeeded(
        DateOnly currentDate,
        IReadOnlyList<FinancialDueAlert> customerAlerts,
        IReadOnlyList<FinancialDueAlert> supplierAlerts) =>
        new(
            true,
            FinancialDueAlertsFailureReason.None,
            currentDate,
            customerAlerts,
            supplierAlerts);
}
