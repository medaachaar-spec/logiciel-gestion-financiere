namespace FinancialManagement.Application.Dashboard;

public interface IFinancialReportsService
{
    Task<FinancialDashboardResult> GetAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken = default);
}
