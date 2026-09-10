using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class ReportsViewModel : ViewModelBase
{
    private const string GenericErrorMessage = "Impossible de charger les rapports.";
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly SKColor SalesColor = new(47, 128, 237);
    private static readonly SKColor PurchasesColor = new(111, 78, 180);
    private static readonly SKColor ExpensesColor = new(220, 119, 70);
    private static readonly SKColor CollectionsColor = new(31, 111, 74);
    private static readonly SKColor PaymentsColor = new(167, 102, 8);

    private readonly IFinancialReportsService _reportsService;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly TimeProvider _timeProvider;
    private DashboardPeriodOption _selectedSynthesisPeriod = null!;
    private DateTime? _customStartDate;
    private DateTime? _customEndDate;
    private ReportMonthOption _periodA = null!;
    private ReportMonthOption _periodB = null!;
    private int _selectedYear;
    private bool _isLoading;
    private bool _hasSynthesisData;
    private bool _hasComparisonData;
    private bool _hasEvolutionData;
    private bool _hasSynthesisActivityData;
    private bool _hasSynthesisCashFlowData;
    private bool _hasEvolutionActivityData;
    private bool _hasEvolutionCashFlowData;
    private string? _errorMessage;
    private string _synthesisPeriodDescription = string.Empty;
    private string _evolutionDescription = string.Empty;
    private ISeries[] _synthesisActivitySeries = [];
    private ISeries[] _synthesisCashFlowSeries = [];
    private ISeries[] _evolutionActivitySeries = [];
    private ISeries[] _evolutionCashFlowSeries = [];
    private Axis[] _synthesisXAxes = [CreateXAxis([])];
    private Axis[] _evolutionXAxes = [CreateXAxis([])];
    private readonly Axis[] _moneyYAxes = [CreateMoneyYAxis()];
    private CancellationTokenSource? _loadCancellation;
    private Task _pendingLoad = Task.CompletedTask;
    private bool _isStopped;

    public ReportsViewModel(
        IFinancialReportsService reportsService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(reportsService);
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _reportsService = reportsService;
        _currentUserSession = currentUserSession;
        _authorizationService = authorizationService;
        _operationCoordinator = operationCoordinator;
        _timeProvider = timeProvider;

        SynthesisPeriodOptions.Add(new DashboardPeriodOption(FinancialPeriodPreset.Today, "Aujourd'hui"));
        SynthesisPeriodOptions.Add(new DashboardPeriodOption(FinancialPeriodPreset.CurrentMonth, "Ce mois"));
        SynthesisPeriodOptions.Add(new DashboardPeriodOption(FinancialPeriodPreset.CurrentYear, "Cette année"));
        SynthesisPeriodOptions.Add(new DashboardPeriodOption(FinancialPeriodPreset.Custom, "Personnalisé"));
        _selectedSynthesisPeriod = SynthesisPeriodOptions[1];

        var today = LocalToday();
        _customStartDate = new DateTime(today.Year, today.Month, 1);
        _customEndDate = _customStartDate.Value.AddMonths(1).AddDays(-1);
        var currentMonth = new DateOnly(today.Year, today.Month, 1);
        for (var offset = 12; offset >= -120; offset--)
            MonthOptions.Add(new ReportMonthOption(currentMonth.AddMonths(offset)));
        _periodA = MonthOptions.Single(option => option.StartDate == currentMonth);
        _periodB = MonthOptions.Single(option => option.StartDate == currentMonth.AddMonths(-1));
        for (var year = today.Year; year >= today.Year - 10; year--) YearOptions.Add(year);
        _selectedYear = today.Year;

        LoadSynthesisCommand = new AsyncRelayCommand(_ => StartLoadAsync(LoadSynthesisCoreAsync), CanLoad);
        LoadComparisonCommand = new AsyncRelayCommand(_ => StartLoadAsync(LoadComparisonCoreAsync), CanLoad);
        LoadEvolutionCommand = new AsyncRelayCommand(_ => StartLoadAsync(LoadEvolutionCoreAsync), CanLoad);
    }

    public ObservableCollection<DashboardPeriodOption> SynthesisPeriodOptions { get; } = [];
    public ObservableCollection<ReportMonthOption> MonthOptions { get; } = [];
    public ObservableCollection<int> YearOptions { get; } = [];
    public ObservableCollection<DashboardMetricViewModel> SynthesisMetrics { get; } = [];
    public ObservableCollection<ReportComparisonRowViewModel> ComparisonRows { get; } = [];
    public ObservableCollection<DashboardMetricViewModel> EvolutionTotals { get; } = [];

    public DashboardPeriodOption SelectedSynthesisPeriod
    {
        get => _selectedSynthesisPeriod;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (SetProperty(ref _selectedSynthesisPeriod, value))
                OnPropertyChanged(nameof(IsCustomSynthesisPeriod));
        }
    }

    public bool IsCustomSynthesisPeriod => SelectedSynthesisPeriod.Preset == FinancialPeriodPreset.Custom;
    public DateTime? CustomStartDate { get => _customStartDate; set => SetProperty(ref _customStartDate, value); }
    public DateTime? CustomEndDate { get => _customEndDate; set => SetProperty(ref _customEndDate, value); }
    public ReportMonthOption PeriodA { get => _periodA; set => SetProperty(ref _periodA, value); }
    public ReportMonthOption PeriodB { get => _periodB; set => SetProperty(ref _periodB, value); }
    public int SelectedYear { get => _selectedYear; set => SetProperty(ref _selectedYear, value); }
    public bool IsLoading { get => _isLoading; private set { if (SetProperty(ref _isLoading, value)) RaiseCommandStates(); } }
    public bool HasSynthesisData { get => _hasSynthesisData; private set => SetProperty(ref _hasSynthesisData, value); }
    public bool HasComparisonData { get => _hasComparisonData; private set => SetProperty(ref _hasComparisonData, value); }
    public bool HasEvolutionData { get => _hasEvolutionData; private set => SetProperty(ref _hasEvolutionData, value); }
    public bool HasSynthesisActivityData { get => _hasSynthesisActivityData; private set => SetProperty(ref _hasSynthesisActivityData, value); }
    public bool HasSynthesisCashFlowData { get => _hasSynthesisCashFlowData; private set => SetProperty(ref _hasSynthesisCashFlowData, value); }
    public bool HasEvolutionActivityData { get => _hasEvolutionActivityData; private set => SetProperty(ref _hasEvolutionActivityData, value); }
    public bool HasEvolutionCashFlowData { get => _hasEvolutionCashFlowData; private set => SetProperty(ref _hasEvolutionCashFlowData, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);
    public string? ErrorMessage { get => _errorMessage; private set { if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); } }
    public string SynthesisPeriodDescription { get => _synthesisPeriodDescription; private set => SetProperty(ref _synthesisPeriodDescription, value); }
    public string EvolutionDescription { get => _evolutionDescription; private set => SetProperty(ref _evolutionDescription, value); }
    public ISeries[] SynthesisActivitySeries { get => _synthesisActivitySeries; private set => SetProperty(ref _synthesisActivitySeries, value); }
    public ISeries[] SynthesisCashFlowSeries { get => _synthesisCashFlowSeries; private set => SetProperty(ref _synthesisCashFlowSeries, value); }
    public ISeries[] EvolutionActivitySeries { get => _evolutionActivitySeries; private set => SetProperty(ref _evolutionActivitySeries, value); }
    public ISeries[] EvolutionCashFlowSeries { get => _evolutionCashFlowSeries; private set => SetProperty(ref _evolutionCashFlowSeries, value); }
    public Axis[] SynthesisXAxes { get => _synthesisXAxes; private set => SetProperty(ref _synthesisXAxes, value); }
    public Axis[] EvolutionXAxes { get => _evolutionXAxes; private set => SetProperty(ref _evolutionXAxes, value); }
    public Axis[] MoneyYAxes => _moneyYAxes;
    public ICommand LoadSynthesisCommand { get; }
    public ICommand LoadComparisonCommand { get; }
    public ICommand LoadEvolutionCommand { get; }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_isStopped || IsLoading) return _pendingLoad;
        return StartLoadAsync(LoadSynthesisCoreAsync, cancellationToken);
    }

    public void Stop()
    {
        if (_isStopped) return;
        _isStopped = true;
        _loadCancellation?.Cancel();
        HasSynthesisData = false;
        HasComparisonData = false;
        HasEvolutionData = false;
        HasSynthesisActivityData = false;
        HasSynthesisCashFlowData = false;
        HasEvolutionActivityData = false;
        HasEvolutionCashFlowData = false;
        RaiseCommandStates();
    }

    public async Task StopAsync()
    {
        Stop();
        await _pendingLoad;
    }

    private bool CanLoad(object? _) => !IsLoading && !_isStopped;

    private Task StartLoadAsync(
        Func<CancellationToken, Task> loader,
        CancellationToken cancellationToken = default)
    {
        if (_isStopped || IsLoading) return _pendingLoad;
        _pendingLoad = LoadSafeAsync(loader, cancellationToken);
        return _pendingLoad;
    }

    private async Task LoadSafeAsync(Func<CancellationToken, Task> loader, CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = cancellation;
        try
        {
            if (!CanAccessReports())
            {
                ErrorMessage = GenericErrorMessage;
                return;
            }

            await loader(cancellation.Token);
        }
        catch (ArgumentException exception)
        {
            ErrorMessage = exception.Message;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Trace.TraceError("Reports loading failed: {0}", exception);
            if (!_isStopped) ErrorMessage = GenericErrorMessage;
        }
        finally
        {
            _loadCancellation = null;
            if (!_isStopped) IsLoading = false;
        }
    }

    private async Task LoadSynthesisCoreAsync(CancellationToken cancellationToken)
    {
        var today = LocalToday();
        var selection = FinancialPeriodSelection.Resolve(
            SelectedSynthesisPeriod.Preset,
            today,
            CustomStartDate.HasValue ? DateOnly.FromDateTime(CustomStartDate.Value) : null,
            CustomEndDate.HasValue ? DateOnly.FromDateTime(CustomEndDate.Value) : null);
        var result = await ReadAsync(new FinancialDashboardQuery(
            selection.StartDate, selection.EndDate,
            Granularity: selection.Granularity,
            IncludeCurrentPositions: false), cancellationToken);
        if (result is null) return;

        ReplaceMetrics(SynthesisMetrics, result);
        SynthesisPeriodDescription = FormatPeriod(selection.StartDate, selection.EndDate);
        ApplyCharts(result.DailyPoints, result.Granularity,
            out var activity, out var cashFlow, out var axes);
        SynthesisActivitySeries = activity;
        SynthesisCashFlowSeries = cashFlow;
        SynthesisXAxes = axes;
        HasSynthesisActivityData = HasActivity(result.DailyPoints);
        HasSynthesisCashFlowData = HasCashFlow(result.DailyPoints);
        HasSynthesisData = true;
    }

    private async Task LoadComparisonCoreAsync(CancellationToken cancellationToken)
    {
        var result = await ReadAsync(new FinancialDashboardQuery(
            PeriodA.StartDate, PeriodA.EndDate,
            PeriodB.StartDate, PeriodB.EndDate,
            IncludeCurrentPositions: false), cancellationToken);
        if (result is null) return;

        ComparisonRows.Clear();
        AddComparison("Ventes", result.TotalSalesMinor, result.ComparisonSalesMinor);
        AddComparison("Achats", result.TotalPurchasesMinor, result.ComparisonPurchasesMinor);
        AddComparison("Dépenses", result.ExpensesMinor, result.ComparisonExpensesMinor);
        AddComparison("Résultat", result.ResultMinor, result.ComparisonResultMinor, isResult: true);
        AddComparison("Encaissements clients", result.CustomerCollectionsMinor, result.ComparisonCustomerCollectionsMinor);
        AddComparison("Paiements fournisseurs", result.SupplierPaymentsMinor, result.ComparisonSupplierPaymentsMinor);
        HasComparisonData = true;
    }

    private async Task LoadEvolutionCoreAsync(CancellationToken cancellationToken)
    {
        var start = new DateOnly(SelectedYear, 1, 1);
        var end = new DateOnly(SelectedYear, 12, 31);
        var result = await ReadAsync(new FinancialDashboardQuery(
            start, end,
            Granularity: FinancialDashboardGranularity.Monthly,
            IncludeCurrentPositions: false), cancellationToken);
        if (result is null) return;

        ReplaceMetrics(EvolutionTotals, result);
        EvolutionDescription = $"Janvier — décembre {SelectedYear}";
        ApplyCharts(result.DailyPoints, FinancialDashboardGranularity.Monthly,
            out var activity, out var cashFlow, out var axes);
        EvolutionActivitySeries = activity;
        EvolutionCashFlowSeries = cashFlow;
        EvolutionXAxes = axes;
        HasEvolutionActivityData = HasActivity(result.DailyPoints);
        HasEvolutionCashFlowData = HasCashFlow(result.DailyPoints);
        HasEvolutionData = true;
    }

    private async Task<FinancialDashboardResult?> ReadAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken)
    {
        var userId = _currentUserSession.UserId;
        var result = await _operationCoordinator.RunAsync(
            token => _reportsService.GetAsync(query, token), cancellationToken);
        if (_isStopped || !CanAccessReports() || _currentUserSession.UserId != userId || !result.Success)
        {
            ErrorMessage = GenericErrorMessage;
            return null;
        }

        ErrorMessage = null;
        return result;
    }

    private bool CanAccessReports() => _currentUserSession.IsAuthenticated
        && _authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.Reports);

    private void AddComparison(string label, long periodA, long periodB, bool isResult = false)
    {
        var percentage = FinancialDashboardComparison.CalculatePercentage(periodA, periodB);
        ComparisonRows.Add(new ReportComparisonRowViewModel(
            label,
            MoneyFormatter.FormatMinor(periodA),
            MoneyFormatter.FormatMinor(periodB),
            percentage.HasValue
                ? $"{percentage.Value.ToString("+0.0;-0.0;0.0", FrenchCulture)} %"
                : "—",
            isResult ? periodA : null,
            isResult ? periodB : null));
    }

    private static void ReplaceMetrics(
        ObservableCollection<DashboardMetricViewModel> target,
        FinancialDashboardResult result)
    {
        target.Clear();
        target.Add(new DashboardMetricViewModel("VENTES", MoneyFormatter.FormatMinor(result.TotalSalesMinor), "Flux de la période", "\uE719"));
        target.Add(new DashboardMetricViewModel("ACHATS", MoneyFormatter.FormatMinor(result.TotalPurchasesMinor), "Flux de la période", "\uE7BF"));
        target.Add(new DashboardMetricViewModel("DÉPENSES", MoneyFormatter.FormatMinor(result.ExpensesMinor), "Flux de la période", "\uE7C1"));
        target.Add(new DashboardMetricViewModel(
            "RÉSULTAT",
            MoneyFormatter.FormatMinor(result.ResultMinor),
            "Ventes − achats − dépenses",
            "\uE9D2",
            result.ResultMinor));
        target.Add(new DashboardMetricViewModel("ENCAISSEMENTS", MoneyFormatter.FormatMinor(result.CustomerCollectionsMinor), "Clients", "\uE8C7"));
        target.Add(new DashboardMetricViewModel("PAIEMENTS", MoneyFormatter.FormatMinor(result.SupplierPaymentsMinor), "Fournisseurs", "\uE8D4"));
    }

    private static void ApplyCharts(
        IReadOnlyList<FinancialDashboardDailyPoint> points,
        FinancialDashboardGranularity granularity,
        out ISeries[] activity,
        out ISeries[] cashFlow,
        out Axis[] axes)
    {
        var dates = points.Select(point => point.Date).ToArray();
        activity =
        [
            CreateLineSeries("Ventes", points.Select(point => point.SalesMinor), dates, granularity, SalesColor),
            CreateLineSeries("Achats", points.Select(point => point.PurchasesMinor), dates, granularity, PurchasesColor),
            CreateLineSeries("Dépenses", points.Select(point => point.ExpensesMinor), dates, granularity, ExpensesColor)
        ];
        cashFlow =
        [
            CreateLineSeries("Encaissements clients", points.Select(point => point.CustomerCollectionsMinor), dates, granularity, CollectionsColor),
            CreateLineSeries("Paiements fournisseurs", points.Select(point => point.SupplierPaymentsMinor), dates, granularity, PaymentsColor)
        ];
        axes = [CreateXAxis(dates.Select(date => date.ToString(
            granularity == FinancialDashboardGranularity.Monthly ? "MMM" : "dd", FrenchCulture)).ToArray())];
    }

    private static LineSeries<long> CreateLineSeries(
        string name, IEnumerable<long> values, IReadOnlyList<DateOnly> dates,
        FinancialDashboardGranularity granularity, SKColor color) => new()
    {
        Name = name,
        Values = values.ToArray(),
        LineSmoothness = dates.Count == 1 ? 0 : 0.35,
        Stroke = new SolidColorPaint(color, 2.5f),
        Fill = null,
        GeometrySize = dates.Count == 1 ? 10 : 5,
        GeometryStroke = new SolidColorPaint(color, 2),
        GeometryFill = new SolidColorPaint(SKColors.White),
        YToolTipLabelFormatter = point => point.Index >= 0 && point.Index < dates.Count
            ? $"{dates[point.Index].ToString(granularity == FinancialDashboardGranularity.Monthly ? "MMMM yyyy" : "dd MMM yyyy", FrenchCulture)} · {MoneyFormatter.FormatMinor(point.Model)}"
            : MoneyFormatter.FormatMinor(point.Model)
    };

    private static Axis CreateXAxis(IReadOnlyList<string> labels) => new()
    {
        Labels = labels.ToArray(),
        LabelsPaint = new SolidColorPaint(new SKColor(97, 112, 125)),
        TextSize = 11,
        MinStep = 1,
        SeparatorsPaint = null
    };

    private static Axis CreateMoneyYAxis() => new()
    {
        MinLimit = 0,
        Labeler = FormatChartAxisAmount,
        LabelsPaint = new SolidColorPaint(new SKColor(97, 112, 125)),
        TextSize = 11,
        Padding = new LiveChartsCore.Drawing.Padding(8),
        SeparatorsPaint = new SolidColorPaint(new SKColor(225, 230, 235)) { StrokeThickness = 1 }
    };

    private static string FormatChartAxisAmount(double value) =>
        ((decimal)value / 100m).ToString("N0", FrenchCulture);

    private static bool HasActivity(IEnumerable<FinancialDashboardDailyPoint> points) =>
        points.Any(point => point.SalesMinor != 0
            || point.PurchasesMinor != 0
            || point.ExpensesMinor != 0);

    private static bool HasCashFlow(IEnumerable<FinancialDashboardDailyPoint> points) =>
        points.Any(point => point.CustomerCollectionsMinor != 0
            || point.SupplierPaymentsMinor != 0);

    private DateOnly LocalToday() => DateOnly.FromDateTime(_timeProvider.GetLocalNow().Date);

    private static string FormatPeriod(DateOnly start, DateOnly end) => start == end
        ? start.ToString("dd MMMM yyyy", FrenchCulture)
        : $"{start:dd/MM/yyyy} — {end:dd/MM/yyyy}";

    private void RaiseCommandStates()
    {
        ((AsyncRelayCommand)LoadSynthesisCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)LoadComparisonCommand).RaiseCanExecuteChanged();
        ((AsyncRelayCommand)LoadEvolutionCommand).RaiseCanExecuteChanged();
    }
}
