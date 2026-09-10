namespace FinancialManagement.Application.Dashboard;

public static class FinancialDashboardComparison
{
    public static decimal? CalculatePercentage(long currentMinor, long comparisonMinor)
    {
        if (comparisonMinor == 0)
        {
            return null;
        }

        return ((decimal)currentMinor - comparisonMinor)
            / Math.Abs((decimal)comparisonMinor) * 100m;
    }
}
