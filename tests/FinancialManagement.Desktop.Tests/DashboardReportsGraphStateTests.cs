using System.Windows;
using System.Windows.Controls;
using FinancialManagement.Application.Alerts;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Desktop.Converters;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Desktop.ViewModels;
using FinancialManagement.Desktop.Views;
using FinancialManagement.Tests;

namespace FinancialManagement.Desktop.Tests;

public sealed class DashboardReportsGraphStateTests
{
    [Fact]
    public async Task PersistedFlows_DriveDashboardAndReportsGraphStates_AndEmptyPeriodsRemainEmpty()
    {
        await using var database = await DashboardSnapshotTestDatabase.CreateAsync();
        await database.CreateSaleAsync(amount: 1_190_000, payment: 820_000);
        await database.CreatePurchaseAsync();

        using var coordinator = new DashboardOperationCoordinator();
        var dashboard = CreateDashboard(database, coordinator);
        await dashboard.LoadAsync();

        AssertPopulatedDashboard(dashboard, "current month");

        var today = DateOnly.FromDateTime(TimeProvider.System.GetLocalNow().Date);
        await SelectDashboardPeriodAsync(dashboard, FinancialPeriodPreset.Today, today, today);
        AssertPopulatedDashboard(dashboard, "today");
        Assert.All(dashboard.ActivitySeries, series => Assert.Equal(10, ((LiveChartsCore.SkiaSharpView.LineSeries<long>)series).GeometrySize));
        Assert.All(dashboard.CashFlowSeries, series => Assert.Equal(10, ((LiveChartsCore.SkiaSharpView.LineSeries<long>)series).GeometrySize));

        await SelectDashboardPeriodAsync(
            dashboard,
            FinancialPeriodPreset.CurrentYear,
            new DateOnly(today.Year, 1, 1),
            new DateOnly(today.Year, 12, 31));
        AssertPopulatedDashboard(dashboard, "current year");

        var reports = CreateReports(database, coordinator);
        await reports.LoadAsync();
        Assert.True(reports.HasSynthesisActivityData);
        Assert.True(reports.HasSynthesisCashFlowData);
        Assert.Equal(3, reports.SynthesisActivitySeries.Length);
        Assert.Equal(2, reports.SynthesisCashFlowSeries.Length);

        await ((AsyncRelayCommand)reports.LoadEvolutionCommand).ExecuteAsync(null);
        Assert.True(reports.HasEvolutionActivityData);
        Assert.True(reports.HasEvolutionCashFlowData);
        Assert.Equal(12, ((LiveChartsCore.SkiaSharpView.LineSeries<long>)reports.EvolutionActivitySeries[0]).Values!.Count());

        AssertRenderedVisibility(dashboard, reports, expectCharts: true, includeEvolution: true);

        var emptyStart = new DateTime(today.Year + 1, 1, 1);
        var emptyDashboard = CreateDashboard(database, coordinator);
        emptyDashboard.SelectPeriodCommand.Execute(
            emptyDashboard.PeriodOptions.Single(option => option.Preset == FinancialPeriodPreset.Custom));
        emptyDashboard.CustomStartDate = emptyStart;
        emptyDashboard.CustomEndDate = emptyStart.AddMonths(1).AddDays(-1);
        await ((AsyncRelayCommand)emptyDashboard.ApplyCustomCommand).ExecuteAsync(null);
        Assert.False(emptyDashboard.HasActivityData);
        Assert.False(emptyDashboard.HasCashFlowData);

        var emptyReports = CreateReports(database, coordinator);
        emptyReports.SelectedSynthesisPeriod = emptyReports.SynthesisPeriodOptions.Single(
            option => option.Preset == FinancialPeriodPreset.Custom);
        emptyReports.CustomStartDate = emptyStart;
        emptyReports.CustomEndDate = emptyStart.AddMonths(1).AddDays(-1);
        await ((AsyncRelayCommand)emptyReports.LoadSynthesisCommand).ExecuteAsync(null);
        Assert.False(emptyReports.HasSynthesisActivityData);
        Assert.False(emptyReports.HasSynthesisCashFlowData);

        AssertRenderedVisibility(emptyDashboard, emptyReports, expectCharts: false, includeEvolution: false);
    }

    [Theory]
    [InlineData(true, Visibility.Collapsed)]
    [InlineData(false, Visibility.Visible)]
    public void InverseBooleanConverter_ProducesVisibilityForVisibilityTargets(
        bool value,
        Visibility expected)
    {
        var converter = new InverseBooleanConverter();

        var converted = converter.Convert(value, typeof(Visibility), null!, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(expected, converted);
    }

    [Fact]
    public void Dashboard_charts_keep_named_controls_inside_polished_chart_surfaces()
    {
        WpfTestHost.Invoke(() =>
        {
            var view = new DashboardView();
            Render(view);

            AssertChartSurface(view, "DashboardActivityChart");
            AssertChartSurface(view, "DashboardCashFlowChart");
        });
    }

    private static DashboardViewModel CreateDashboard(
        DashboardSnapshotTestDatabase database,
        DashboardOperationCoordinator coordinator)
    {
        var dueAlerts = new DueAlertsViewModel(
            database.Get<IFinancialDueAlertService>(),
            database.Get<ICurrentUserSession>(),
            database.Get<IAuthorizationService>(),
            coordinator);
        return new DashboardViewModel(
            database.Dashboard,
            database.Get<ICurrentUserSession>(),
            database.Get<IAuthorizationService>(),
            TimeProvider.System,
            dueAlerts,
            coordinator);
    }

    private static ReportsViewModel CreateReports(
        DashboardSnapshotTestDatabase database,
        DashboardOperationCoordinator coordinator) =>
        new(
            database.Get<IFinancialReportsService>(),
            database.Get<ICurrentUserSession>(),
            database.Get<IAuthorizationService>(),
            coordinator,
            TimeProvider.System);

    private static async Task SelectDashboardPeriodAsync(
        DashboardViewModel dashboard,
        FinancialPeriodPreset preset,
        DateOnly expectedStart,
        DateOnly expectedEnd)
    {
        dashboard.SelectPeriodCommand.Execute(
            dashboard.PeriodOptions.Single(option => option.Preset == preset));
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeout)
        {
            if (!dashboard.IsLoading
                && dashboard.DashboardData?.FilterStartDate == expectedStart
                && dashboard.DashboardData.FilterEndDate == expectedEnd)
            {
                return;
            }

            await Task.Delay(20);
        }

        Assert.Fail($"Dashboard did not finish loading {preset}.");
    }

    private static void AssertPopulatedDashboard(DashboardViewModel dashboard, string period)
    {
        Assert.True(dashboard.HasActivityData, $"Activity should be visible for {period}.");
        Assert.True(dashboard.HasCashFlowData, $"Cash flow should be visible for {period}.");
        Assert.Equal(3, dashboard.ActivitySeries.Length);
        Assert.Equal(2, dashboard.CashFlowSeries.Length);
    }

    private static void AssertRenderedVisibility(
        DashboardViewModel dashboard,
        ReportsViewModel reports,
        bool expectCharts,
        bool includeEvolution)
    {
        WpfTestHost.Invoke(() =>
        {
            var dashboardView = new DashboardView { DataContext = dashboard };
            Render(dashboardView);
            AssertPair(
                dashboardView.FindName("DashboardActivityChart") as FrameworkElement,
                dashboardView.FindName("DashboardActivityEmptyState") as FrameworkElement,
                expectCharts);
            AssertPair(
                dashboardView.FindName("DashboardCashFlowChart") as FrameworkElement,
                dashboardView.FindName("DashboardCashFlowEmptyState") as FrameworkElement,
                expectCharts);

            var reportsView = new ReportsView { DataContext = reports };
            Render(reportsView);
            AssertPair(
                reportsView.FindName("ReportsSynthesisActivityChart") as FrameworkElement,
                reportsView.FindName("ReportsSynthesisActivityEmptyState") as FrameworkElement,
                expectCharts);
            AssertPair(
                reportsView.FindName("ReportsSynthesisCashFlowChart") as FrameworkElement,
                reportsView.FindName("ReportsSynthesisCashFlowEmptyState") as FrameworkElement,
                expectCharts);
            if (includeEvolution)
            {
                AssertPair(
                    reportsView.FindName("ReportsEvolutionActivityChart") as FrameworkElement,
                    reportsView.FindName("ReportsEvolutionActivityEmptyState") as FrameworkElement,
                    expectCharts);
                AssertPair(
                    reportsView.FindName("ReportsEvolutionCashFlowChart") as FrameworkElement,
                    reportsView.FindName("ReportsEvolutionCashFlowEmptyState") as FrameworkElement,
                    expectCharts);
            }

            dashboardView.DataContext = null;
            reportsView.DataContext = null;
        });
    }

    private static void Render(FrameworkElement view)
    {
        view.Measure(new Size(1100, 700));
        view.Arrange(new Rect(0, 0, 1100, 700));
        view.UpdateLayout();
    }

    private static void AssertPair(FrameworkElement? chart, FrameworkElement? emptyState, bool expectCharts)
    {
        Assert.NotNull(chart);
        Assert.NotNull(emptyState);
        Assert.Equal(expectCharts ? Visibility.Visible : Visibility.Collapsed, chart.Visibility);
        Assert.Equal(expectCharts ? Visibility.Collapsed : Visibility.Visible, emptyState.Visibility);
    }

    private static void AssertChartSurface(FrameworkElement view, string chartName)
    {
        var chart = Assert.IsAssignableFrom<FrameworkElement>(view.FindName(chartName));
        var chartGrid = Assert.IsType<Grid>(chart.Parent);
        var chartSurface = Assert.IsType<Border>(chartGrid.Parent);

        Assert.Equal(new CornerRadius(12), chartSurface.CornerRadius);
        Assert.Equal(new Thickness(8, 6, 8, 2), chartSurface.Padding);
        Assert.Equal(new Thickness(1), chartSurface.BorderThickness);
    }
}
