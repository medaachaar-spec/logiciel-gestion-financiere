namespace FinancialManagement.Application.Dashboard;

public interface IFinancialDashboardService
{
    Task<FinancialDashboardResult> GetAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken = default);
}
