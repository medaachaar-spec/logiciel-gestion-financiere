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

public sealed class DashboardViewModel : ViewModelBase
{
    private const string GenericErrorMessage = "Impossible de charger le tableau de bord.";
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");
    private static readonly SKColor SalesColor = new(47, 128, 237);
    private static readonly SKColor PurchasesColor = new(111, 78, 180);
    private static readonly SKColor ExpensesColor = new(220, 119, 70);
    private static readonly SKColor CollectionsColor = new(31, 111, 74);
    private static readonly SKColor PaymentsColor = new(167, 102, 8);

    private readonly IFinancialDashboardService _dashboardService;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly TimeProvider _timeProvider;
    private readonly AsyncRelayCommand _refreshRelayCommand;
    private readonly AsyncRelayCommand _applyCustomRelayCommand;
    private DateOnly _knownLocalDate;
    private FinancialPeriodPreset _selectedPreset = FinancialPeriodPreset.CurrentMonth;
    private DateTime? _customStartDate;
    private DateTime? _customEndDate;
    private bool _isLoading;
    private bool _hasData;
    private bool _hasActivityData;
    private bool _hasCashFlowData;
    private string? _errorMessage;
    private FinancialDashboardResult? _dashboardData;
    private string _periodDescription = string.Empty;
    private string _chartGranularityDescription = "Activité quotidienne";
    private string _customerReceivablesTotal = MoneyFormatter.FormatMinor(0);
    private string _supplierPayablesTotal = MoneyFormatter.FormatMinor(0);
    private string _customerCollectionsTotal = MoneyFormatter.FormatMinor(0);
    private string _supplierPaymentsTotal = MoneyFormatter.FormatMinor(0);
    private string _customerReceivableCountDescription = "Aucun client concerné";
    private string _supplierPayableCountDescription = "Aucun fournisseur concerné";
    private ISeries[] _activitySeries = [];
    private ISeries[] _cashFlowSeries = [];
    private Axis[] _activityXAxes = [CreateXAxis([])];
    private Axis[] _cashFlowXAxes = [CreateXAxis([])];
    private readonly Axis[] _moneyYAxes = [CreateMoneyYAxis()];
    private bool _isStopped;
    private CancellationTokenSource? _loadCancellation;
    private Task _pendingLoad = Task.CompletedTask;

    public DashboardViewModel(
        IFinancialDashboardService dashboardService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        TimeProvider timeProvider,
        DueAlertsViewModel dueAlerts,
        DashboardOperationCoordinator operationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(dashboardService);
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(dueAlerts);
        ArgumentNullException.ThrowIfNull(operationCoordinator);

        _dashboardService = dashboardService;
        _currentUserSession = currentUserSession;
        _authorizationService = authorizationService;
        _timeProvider = timeProvider;
        _operationCoordinator = operationCoordinator;
        DueAlerts = dueAlerts;

        PeriodOptions.Add(new DashboardPeriodOption(FinancialPeriodPreset.Today, "Aujourd'hui"));
        PeriodOptions.Add(new DashboardPeriodOption(FinancialPeriodPreset.CurrentMonth, "Ce mois"));
        PeriodOptions.Add(new DashboardPeriodOption(FinancialPeriodPreset.CurrentYear, "Cette année"));
        PeriodOptions.Add(new DashboardPeriodOption(FinancialPeriodPreset.Custom, "Personnalisé"));

        _refreshRelayCommand = new AsyncRelayCommand(
            _ => RefreshAsync(includeDueAlerts: true),
            _ => !IsLoading && !_isStopped);
        _applyCustomRelayCommand = new AsyncRelayCommand(
            _ => RefreshAsync(includeDueAlerts: false),
            _ => !IsLoading && !_isStopped && IsCustomPeriod
                && CustomStartDate.HasValue && CustomEndDate.HasValue);
        RefreshCommand = _refreshRelayCommand;
        ApplyCustomCommand = _applyCustomRelayCommand;
        SelectPeriodCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is not DashboardPeriodOption option || IsLoading || _isStopped) return;
                SelectPreset(option.Preset);
                if (option.Preset != FinancialPeriodPreset.Custom)
                    _ = RefreshAsync(includeDueAlerts: false);
            });
        OpenCustomerReceivablesCommand = new RelayCommand(
            _ => NavigationRequested?.Invoke(this, "customer-payments"));
        OpenSupplierPayablesCommand = new RelayCommand(
            _ => NavigationRequested?.Invoke(this, "supplier-payments"));
        InitializeCurrentMonth();
    }

    public ObservableCollection<DashboardPeriodOption> PeriodOptions { get; } = [];

    public DateTime? CustomStartDate
    {
        get => _customStartDate;
        set
        {
            if (SetProperty(ref _customStartDate, value))
                _applyCustomRelayCommand.RaiseCanExecuteChanged();
        }
    }

    public DateTime? CustomEndDate
    {
        get => _customEndDate;
        set
        {
            if (SetProperty(ref _customEndDate, value))
                _applyCustomRelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool IsCustomPeriod => _selectedPreset == FinancialPeriodPreset.Custom;

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (!SetProperty(ref _isLoading, value)) return;
            _refreshRelayCommand.RaiseCanExecuteChanged();
            _applyCustomRelayCommand.RaiseCanExecuteChanged();
        }
    }

    public bool HasData { get => _hasData; private set => SetProperty(ref _hasData, value); }
    public bool HasActivityData { get => _hasActivityData; private set => SetProperty(ref _hasActivityData, value); }
    public bool HasCashFlowData { get => _hasCashFlowData; private set => SetProperty(ref _hasCashFlowData, value); }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError));
        }
    }

    public string PeriodDescription
    {
        get => _periodDescription;
        private set => SetProperty(ref _periodDescription, value);
    }

    public string ChartGranularityDescription
    {
        get => _chartGranularityDescription;
        private set => SetProperty(ref _chartGranularityDescription, value);
    }

    public string TreasuryStatus => DashboardData is not { } data ? string.Empty
        : !data.TreasuryIsInitialized
            ? "Trésorerie non initialisée — les soldes n'incluent aucun solde d'ouverture."
            : data.TreasuryOpeningDate is DateOnly date
                ? $"Soldes actuels · initialisés le {date:dd/MM/yyyy}"
                : "Soldes actuels";

    public FinancialDashboardResult? DashboardData
    {
        get => _dashboardData;
        private set
        {
            if (SetProperty(ref _dashboardData, value)) OnPropertyChanged(nameof(TreasuryStatus));
        }
    }

    public ObservableCollection<DashboardMetricViewModel> PrimaryMetrics { get; } = [];
    public ObservableCollection<DashboardMetricViewModel> CurrentMetrics { get; } = [];
    public ObservableCollection<DashboardPartyBalanceViewModel> TopCustomerReceivables { get; } = [];
    public ObservableCollection<DashboardPartyBalanceViewModel> TopSupplierPayables { get; } = [];

    public string CustomerReceivablesTotal { get => _customerReceivablesTotal; private set => SetProperty(ref _customerReceivablesTotal, value); }
    public string SupplierPayablesTotal { get => _supplierPayablesTotal; private set => SetProperty(ref _supplierPayablesTotal, value); }
    public string CustomerCollectionsTotal { get => _customerCollectionsTotal; private set => SetProperty(ref _customerCollectionsTotal, value); }
    public string SupplierPaymentsTotal { get => _supplierPaymentsTotal; private set => SetProperty(ref _supplierPaymentsTotal, value); }
    public string CustomerReceivableCountDescription { get => _customerReceivableCountDescription; private set => SetProperty(ref _customerReceivableCountDescription, value); }
    public string SupplierPayableCountDescription { get => _supplierPayableCountDescription; private set => SetProperty(ref _supplierPayableCountDescription, value); }
    public ISeries[] ActivitySeries { get => _activitySeries; private set => SetProperty(ref _activitySeries, value); }
    public ISeries[] CashFlowSeries { get => _cashFlowSeries; private set => SetProperty(ref _cashFlowSeries, value); }
    public Axis[] ActivityXAxes { get => _activityXAxes; private set => SetProperty(ref _activityXAxes, value); }
    public Axis[] CashFlowXAxes { get => _cashFlowXAxes; private set => SetProperty(ref _cashFlowXAxes, value); }
    public Axis[] MoneyYAxes => _moneyYAxes;
    public DueAlertsViewModel DueAlerts { get; }
    public ICommand SelectPeriodCommand { get; }
    public ICommand ApplyCustomCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand OpenCustomerReceivablesCommand { get; }
    public ICommand OpenSupplierPayablesCommand { get; }
    public event EventHandler<string>? NavigationRequested;

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_isStopped || IsLoading) return _pendingLoad;
        RefreshPresetForLocalDateIfNeeded();
        _pendingLoad = RefreshAsync(includeDueAlerts: true, cancellationToken);
        return _pendingLoad;
    }

    public Task RefreshAsync(bool includeDueAlerts = false, CancellationToken cancellationToken = default)
    {
        if (_isStopped || IsLoading) return _pendingLoad;
        _pendingLoad = LoadDashboardAndAlertsAsync(includeDueAlerts, cancellationToken);
        return _pendingLoad;
    }

    public void Stop()
    {
        if (_isStopped) return;
        _isStopped = true;
        DueAlerts.Stop();
        ClearDashboardData();
        ErrorMessage = null;
        IsLoading = false;
        _loadCancellation?.Cancel();
    }

    public async Task StopAsync()
    {
        Stop();
        await Task.WhenAll(_pendingLoad, DueAlerts.StopAsync());
    }

    private async Task LoadDashboardAndAlertsAsync(bool includeDueAlerts, CancellationToken cancellationToken)
    {
        await LoadCoreAsync(cancellationToken);
        if (includeDueAlerts && !_isStopped) await DueAlerts.RefreshAsync(cancellationToken);
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = cancellation;
        try
        {
            if (!_currentUserSession.IsAuthenticated
                || !_authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.GlobalDashboard))
            {
                ClearDashboardData();
                ErrorMessage = GenericErrorMessage;
                return;
            }

            FinancialPeriodSelection selection;
            try
            {
                selection = ResolveSelection();
            }
            catch (ArgumentException exception)
            {
                ClearDashboardData();
                ErrorMessage = exception.Message;
                return;
            }

            var query = new FinancialDashboardQuery(
                selection.StartDate,
                selection.EndDate,
                Granularity: selection.Granularity);
            var userId = _currentUserSession.UserId;
            var result = await _operationCoordinator.RunAsync(
                token => _dashboardService.GetAsync(query, token), cancellation.Token);
            if (_isStopped || cancellation.IsCancellationRequested) return;
            if (!_currentUserSession.IsAuthenticated || _currentUserSession.UserId != userId
                || !_authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.GlobalDashboard)
                || !result.Success)
            {
                ClearDashboardData();
                ErrorMessage = GenericErrorMessage;
                return;
            }

            ApplyResult(result, selection);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception exception)
        {
            Trace.TraceError("Dashboard loading failed: {0}", exception);
            if (!_isStopped)
            {
                ClearDashboardData();
                ErrorMessage = GenericErrorMessage;
            }
        }
        finally
        {
            _loadCancellation = null;
            if (!_isStopped) IsLoading = false;
        }
    }

    private void ApplyResult(FinancialDashboardResult result, FinancialPeriodSelection selection)
    {
        DashboardData = result;
        HasData = true;
        PeriodDescription = FormatPeriod(selection);
        ChartGranularityDescription = selection.Granularity == FinancialDashboardGranularity.Monthly
            ? "Activité mensuelle"
            : selection.StartDate == selection.EndDate ? "Activité du jour" : "Activité quotidienne";

        PrimaryMetrics.Clear();
        PrimaryMetrics.Add(CreateFlowMetric("VENTES", "\uE719", result.TotalSalesMinor));
        PrimaryMetrics.Add(CreateFlowMetric("ACHATS", "\uE7BF", result.TotalPurchasesMinor));
        PrimaryMetrics.Add(CreateFlowMetric("DÉPENSES", "\uE7C1", result.ExpensesMinor));
        PrimaryMetrics.Add(CreateFlowMetric("RÉSULTAT", "\uE9D2", result.ResultMinor, isResult: true));

        CurrentMetrics.Clear();
        CurrentMetrics.Add(new DashboardMetricViewModel("CRÉANCES CLIENTS", MoneyFormatter.FormatMinor(result.CurrentCustomerReceivablesMinor), "Position actuelle", "\uE77B"));
        CurrentMetrics.Add(new DashboardMetricViewModel("DETTES FOURNISSEURS", MoneyFormatter.FormatMinor(result.CurrentSupplierPayablesMinor), "Position actuelle", "\uE716"));
        CurrentMetrics.Add(new DashboardMetricViewModel("CAISSE", MoneyFormatter.FormatMinor(result.CurrentCashBalanceMinor), "Position actuelle", "\uE8C7"));
        CurrentMetrics.Add(new DashboardMetricViewModel("BANQUE", MoneyFormatter.FormatMinor(result.CurrentBankBalanceMinor), "Position actuelle", "\uE8A1"));
        CurrentMetrics.Add(new DashboardMetricViewModel("TRÉSORERIE TOTALE", MoneyFormatter.FormatMinor(result.CurrentTotalTreasuryMinor), "Caisse + banque", "\uE8D4"));

        ReplaceBalances(TopCustomerReceivables, result.TopCustomerReceivables);
        ReplaceBalances(TopSupplierPayables, result.TopSupplierPayables);
        CustomerReceivablesTotal = MoneyFormatter.FormatMinor(result.CurrentCustomerReceivablesMinor);
        SupplierPayablesTotal = MoneyFormatter.FormatMinor(result.CurrentSupplierPayablesMinor);
        CustomerCollectionsTotal = MoneyFormatter.FormatMinor(result.CustomerCollectionsMinor);
        SupplierPaymentsTotal = MoneyFormatter.FormatMinor(result.SupplierPaymentsMinor);
        CustomerReceivableCountDescription = FormatPartyCount(result.CustomerReceivablePartyCount, "client concerné", "clients concernés");
        SupplierPayableCountDescription = FormatPartyCount(result.SupplierPayablePartyCount, "fournisseur concerné", "fournisseurs concernés");
        ApplyCharts(result.DailyPoints, result.Granularity);
        ErrorMessage = null;
    }

    private static DashboardMetricViewModel CreateFlowMetric(
        string title,
        string iconGlyph,
        long amountMinor,
        bool isResult = false) =>
        new(
            title,
            MoneyFormatter.FormatMinor(amountMinor),
            "Flux de la période",
            iconGlyph,
            isResult ? amountMinor : null);

    private static void ReplaceBalances(
        ObservableCollection<DashboardPartyBalanceViewModel> target,
        IEnumerable<FinancialDashboardPartyBalance> source)
    {
        target.Clear();
        foreach (var balance in source) target.Add(new DashboardPartyBalanceViewModel(balance));
    }

    private void ApplyCharts(
        IReadOnlyList<FinancialDashboardDailyPoint> points,
        FinancialDashboardGranularity granularity)
    {
        var dates = points.Select(point => point.Date).ToArray();
        var labels = dates.Select(date => granularity == FinancialDashboardGranularity.Monthly
            ? date.ToString("MMM", FrenchCulture)
            : date.ToString("dd", FrenchCulture)).ToArray();
        ActivitySeries =
        [
            CreateLineSeries("Ventes", points.Select(point => point.SalesMinor), dates, granularity, SalesColor, true),
            CreateLineSeries("Achats", points.Select(point => point.PurchasesMinor), dates, granularity, PurchasesColor),
            CreateLineSeries("Dépenses", points.Select(point => point.ExpensesMinor), dates, granularity, ExpensesColor)
        ];
        CashFlowSeries =
        [
            CreateLineSeries("Encaissements clients", points.Select(point => point.CustomerCollectionsMinor), dates, granularity, CollectionsColor, true),
            CreateLineSeries("Paiements fournisseurs", points.Select(point => point.SupplierPaymentsMinor), dates, granularity, PaymentsColor)
        ];
        ActivityXAxes = [CreateXAxis(labels)];
        CashFlowXAxes = [CreateXAxis(labels)];
        HasActivityData = points.Any(point => point.SalesMinor != 0 || point.PurchasesMinor != 0 || point.ExpensesMinor != 0);
        HasCashFlowData = points.Any(point => point.CustomerCollectionsMinor != 0 || point.SupplierPaymentsMinor != 0);
    }

    private static LineSeries<long> CreateLineSeries(
        string name, IEnumerable<long> values, IReadOnlyList<DateOnly> dates,
        FinancialDashboardGranularity granularity, SKColor color, bool withFill = false) =>
        new()
        {
            Name = name,
            Values = values.ToArray(),
            LineSmoothness = dates.Count == 1 ? 0 : 0.35,
            Stroke = new SolidColorPaint(color, 2.5f),
            Fill = withFill ? new SolidColorPaint(color.WithAlpha(22)) : null,
            GeometrySize = dates.Count == 1 ? 10 : 5,
            GeometryStroke = new SolidColorPaint(color, 2),
            GeometryFill = new SolidColorPaint(SKColors.White),
            YToolTipLabelFormatter = point =>
            {
                var dateLabel = point.Index >= 0 && point.Index < dates.Count
                    ? dates[point.Index].ToString(
                        granularity == FinancialDashboardGranularity.Monthly ? "MMMM yyyy" : "dd MMM yyyy",
                        FrenchCulture)
                    : string.Empty;
                return $"{dateLabel} · {MoneyFormatter.FormatMinor(point.Model)}";
            }
        };

    private static Axis CreateXAxis(IReadOnlyList<string> labels) => new()
    {
        Labels = labels.ToArray(),
        LabelsPaint = new SolidColorPaint(new SKColor(97, 112, 125)),
        TextSize = 11,
        MinStep = 1,
        SeparatorsPaint = null,
        Padding = new LiveChartsCore.Drawing.Padding(8)
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

    private void InitializeCurrentMonth()
    {
        _knownLocalDate = LocalToday();
        _customStartDate = new DateTime(_knownLocalDate.Year, _knownLocalDate.Month, 1);
        _customEndDate = _customStartDate.Value.AddMonths(1).AddDays(-1);
        SelectPreset(FinancialPeriodPreset.CurrentMonth);
    }

    private void SelectPreset(FinancialPeriodPreset preset)
    {
        _selectedPreset = preset;
        foreach (var option in PeriodOptions) option.SetSelected(option.Preset == preset);
        OnPropertyChanged(nameof(IsCustomPeriod));
        _applyCustomRelayCommand.RaiseCanExecuteChanged();
    }

    private void RefreshPresetForLocalDateIfNeeded()
    {
        if (_selectedPreset == FinancialPeriodPreset.Custom) return;
        var localToday = LocalToday();
        if (localToday == _knownLocalDate) return;
        _knownLocalDate = localToday;
        SelectPreset(_selectedPreset);
    }

    private FinancialPeriodSelection ResolveSelection() => FinancialPeriodSelection.Resolve(
        _selectedPreset,
        LocalToday(),
        CustomStartDate.HasValue ? DateOnly.FromDateTime(CustomStartDate.Value) : null,
        CustomEndDate.HasValue ? DateOnly.FromDateTime(CustomEndDate.Value) : null);

    private DateOnly LocalToday() => DateOnly.FromDateTime(_timeProvider.GetLocalNow().Date);

    private static string FormatPeriod(FinancialPeriodSelection selection) =>
        selection.StartDate == selection.EndDate
            ? selection.StartDate.ToString("dd MMMM yyyy", FrenchCulture)
            : $"{selection.StartDate:dd/MM/yyyy} — {selection.EndDate:dd/MM/yyyy}";

    private void ClearDashboardData()
    {
        DashboardData = null;
        HasData = false;
        HasActivityData = false;
        HasCashFlowData = false;
        PrimaryMetrics.Clear();
        CurrentMetrics.Clear();
        TopCustomerReceivables.Clear();
        TopSupplierPayables.Clear();
        ActivitySeries = [];
        CashFlowSeries = [];
        CustomerCollectionsTotal = MoneyFormatter.FormatMinor(0);
        SupplierPaymentsTotal = MoneyFormatter.FormatMinor(0);
    }

    private static string FormatPartyCount(int count, string singular, string plural) =>
        count == 0 ? $"Aucun {singular}" : $"{count} {(count == 1 ? singular : plural)}";
}
