namespace FinancialManagement.Application.Dashboard;

public sealed record FinancialDashboardQuery(
    DateOnly? StartDate,
    DateOnly? EndDate);
