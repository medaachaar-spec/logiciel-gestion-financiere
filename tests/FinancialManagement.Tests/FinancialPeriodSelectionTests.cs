using FinancialManagement.Application.Dashboard;

namespace FinancialManagement.Tests;

public sealed class FinancialPeriodSelectionTests
{
    private static readonly DateOnly Today = new(2026, 9, 9);

    [Fact]
    public void Approved_presets_explicitly_exclude_weekly_mode()
    {
        Assert.Equal(
            [
                FinancialPeriodPreset.Today,
                FinancialPeriodPreset.CurrentMonth,
                FinancialPeriodPreset.CurrentYear,
                FinancialPeriodPreset.Custom
            ],
            Enum.GetValues<FinancialPeriodPreset>());
    }

    [Theory]
    [InlineData(FinancialPeriodPreset.Today, "2026-09-09", "2026-09-09", FinancialDashboardGranularity.Daily)]
    [InlineData(FinancialPeriodPreset.CurrentMonth, "2026-09-01", "2026-09-30", FinancialDashboardGranularity.Daily)]
    [InlineData(FinancialPeriodPreset.CurrentYear, "2026-01-01", "2026-12-31", FinancialDashboardGranularity.Monthly)]
    public void Presets_resolve_against_the_current_local_business_date(
        FinancialPeriodPreset preset,
        string expectedStart,
        string expectedEnd,
        FinancialDashboardGranularity expectedGranularity)
    {
        var selection = FinancialPeriodSelection.Resolve(preset, Today);

        Assert.Equal(DateOnly.Parse(expectedStart), selection.StartDate);
        Assert.Equal(DateOnly.Parse(expectedEnd), selection.EndDate);
        Assert.Equal(expectedGranularity, selection.Granularity);
    }

    [Theory]
    [InlineData("2026-01-01", "2026-03-03", FinancialDashboardGranularity.Daily)]
    [InlineData("2026-01-01", "2026-03-04", FinancialDashboardGranularity.Monthly)]
    public void Custom_range_uses_daily_through_62_days_then_monthly(
        string start,
        string end,
        FinancialDashboardGranularity expected)
    {
        var selection = FinancialPeriodSelection.Resolve(
            FinancialPeriodPreset.Custom,
            Today,
            DateOnly.Parse(start),
            DateOnly.Parse(end));

        Assert.Equal(expected, selection.Granularity);
    }

    [Fact]
    public void Custom_range_requires_ordered_explicit_dates()
    {
        Assert.Throws<ArgumentException>(() => FinancialPeriodSelection.Resolve(
            FinancialPeriodPreset.Custom, Today));
        Assert.Throws<ArgumentException>(() => FinancialPeriodSelection.Resolve(
            FinancialPeriodPreset.Custom, Today, Today, Today.AddDays(-1)));
    }
}
