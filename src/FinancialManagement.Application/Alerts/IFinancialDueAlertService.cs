namespace FinancialManagement.Application.Alerts;

public interface IFinancialDueAlertService
{
    Task<FinancialDueAlertsResult> GetAlertsAsync(
        FinancialDueAlertsQuery query,
        CancellationToken cancellationToken = default);
}
