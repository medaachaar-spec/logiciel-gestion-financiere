namespace FinancialManagement.Application.Dashboard;

public sealed record FinancialDashboardQuery(
    DateOnly? StartDate,
    DateOnly? EndDate,
    DateOnly? ComparisonStartDate = null,
    DateOnly? ComparisonEndDate = null,
    FinancialDashboardGranularity Granularity = FinancialDashboardGranularity.Daily,
    bool IncludeCurrentPositions = true);
