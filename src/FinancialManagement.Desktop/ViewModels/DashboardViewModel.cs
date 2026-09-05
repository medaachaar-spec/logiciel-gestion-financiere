using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Services;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class DashboardViewModel : ViewModelBase
{
    private const string InvalidDateRangeMessage =
        "La date de début doit être antérieure ou égale à la date de fin.";
    private const string MissingDateMessage =
        "Veuillez sélectionner une date de début et une date de fin.";
    private const string GenericErrorMessage =
        "Impossible de charger le tableau de bord.";

    private readonly IFinancialDashboardService _dashboardService;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly AsyncRelayCommand _refreshRelayCommand;
    private DateTime? _startDate;
    private DateTime? _endDate;
    private bool _isLoading;
    private bool _hasData;
    private string? _errorMessage;
    private FinancialDashboardResult? _dashboardData;
    private bool _initialLoadRequested;
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
        _operationCoordinator = operationCoordinator;
        DueAlerts = dueAlerts;

        var localToday = timeProvider.GetLocalNow().Date;
        _startDate = new DateTime(localToday.Year, localToday.Month, 1);
        _endDate = localToday;

        _refreshRelayCommand = new AsyncRelayCommand(
            _ => RefreshAsync(),
            _ => !IsLoading && !_isStopped);
        RefreshCommand = _refreshRelayCommand;
        ApplyCommand = _refreshRelayCommand;
    }

    public DateTime? StartDate
    {
        get => _startDate;
        set
        {
            if (SetProperty(ref _startDate, value))
            {
                ClearDateValidationMessage();
            }
        }
    }

    public DateTime? EndDate
    {
        get => _endDate;
        set
        {
            if (SetProperty(ref _endDate, value))
            {
                ClearDateValidationMessage();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                _refreshRelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasData
    {
        get => _hasData;
        private set => SetProperty(ref _hasData, value);
    }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasError));
            }
        }
    }

    public string PeriodDescription =>
        DashboardData?.FilterStartDate is DateOnly startDate && DashboardData.FilterEndDate is DateOnly endDate
            ? $"Du {startDate:dd/MM/yyyy} au {endDate:dd/MM/yyyy}"
            : DashboardData is null ? "Aucune période chargée" : "Période sans borne complète";

    public string TreasuryStatus => DashboardData is not { } data ? string.Empty
        : !data.TreasuryIsInitialized
            ? "Trésorerie non initialisée — les soldes affichés ne comprennent pas de soldes d'ouverture."
            : data.TreasuryOpeningDate is DateOnly date
                ? $"Trésorerie initialisée le {date:dd/MM/yyyy}."
                : "Trésorerie initialisée.";

    public FinancialDashboardResult? DashboardData
    {
        get => _dashboardData;
        private set
        {
            if (SetProperty(ref _dashboardData, value))
            {
                OnPropertyChanged(nameof(PeriodDescription));
                OnPropertyChanged(nameof(TreasuryStatus));
            }
        }
    }

    public ObservableCollection<DashboardMetricViewModel> ActivityMetrics { get; } = [];

    public ObservableCollection<DashboardMetricViewModel> CurrentMetrics { get; } = [];

    public DueAlertsViewModel DueAlerts { get; }

    public ICommand ApplyCommand { get; }

    public ICommand RefreshCommand { get; }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_initialLoadRequested || _isStopped)
        {
            return _pendingLoad;
        }

        _initialLoadRequested = true;
        _pendingLoad = LoadInitialAsync(cancellationToken);
        return _pendingLoad;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_isStopped || IsLoading)
        {
            return _pendingLoad;
        }

        _initialLoadRequested = true;
        _pendingLoad = LoadCoreAsync(cancellationToken);
        return _pendingLoad;
    }

    // Stop synchronously hides data; the owner awaits completion before disposing its scope.
    public void Stop()
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;
        DueAlerts.Stop();
        ClearDashboardData();
        ErrorMessage = null;
        IsLoading = false;
        _refreshRelayCommand.RaiseCanExecuteChanged();
        _loadCancellation?.Cancel();
    }

    public async Task StopAsync()
    {
        Stop();
        await Task.WhenAll(_pendingLoad, DueAlerts.StopAsync());
    }

    private async Task LoadInitialAsync(CancellationToken cancellationToken)
    {
        await LoadCoreAsync(cancellationToken);
        if (!_isStopped)
        {
            await DueAlerts.LoadAsync(cancellationToken);
        }
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ClearDashboardData();
        ErrorMessage = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = cancellation;

        try
        {
            if (!_currentUserSession.IsAuthenticated
                || !_authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.GlobalDashboard))
            {
                ErrorMessage = GenericErrorMessage;
                return;
            }

            if (!TryCreateQuery(out var query))
            {
                return;
            }

            var userId = _currentUserSession.UserId;
            var result = await _operationCoordinator.RunAsync(
                token => _dashboardService.GetAsync(query, token),
                cancellation.Token);
            if (_isStopped || cancellation.IsCancellationRequested)
            {
                return;
            }

            if (!_currentUserSession.IsAuthenticated || _currentUserSession.UserId != userId
                || !_authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.GlobalDashboard))
            {
                ErrorMessage = GenericErrorMessage;
                return;
            }

            if (!result.Success)
            {
                ClearDashboardData();
                ErrorMessage = GenericErrorMessage;
                return;
            }

            ApplyResult(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the owning Shell is being closed.
        }
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
            if (!_isStopped)
            {
                IsLoading = false;
            }
        }
    }

    private bool TryCreateQuery(out FinancialDashboardQuery query)
    {
        query = null!;

        if (StartDate is not DateTime startDate || EndDate is not DateTime endDate)
        {
            ClearDashboardData();
            ErrorMessage = MissingDateMessage;
            return false;
        }

        if (startDate.Date > endDate.Date)
        {
            ClearDashboardData();
            ErrorMessage = InvalidDateRangeMessage;
            return false;
        }

        query = new FinancialDashboardQuery(
            DateOnly.FromDateTime(startDate.Date),
            DateOnly.FromDateTime(endDate.Date));
        return true;
    }

    private void ApplyResult(FinancialDashboardResult result)
    {
        DashboardData = result;
        HasData = true;
        ActivityMetrics.Clear();
        ActivityMetrics.Add(new DashboardMetricViewModel(
            "Ventes",
            MoneyFormatter.FormatMinor(result.TotalSalesMinor),
            "Flux sur la période"));
        ActivityMetrics.Add(new DashboardMetricViewModel(
            "Achats",
            MoneyFormatter.FormatMinor(result.TotalPurchasesMinor),
            "Flux sur la période"));
        ActivityMetrics.Add(new DashboardMetricViewModel(
            "Encaissements clients",
            MoneyFormatter.FormatMinor(result.CustomerCollectionsMinor),
            "Flux sur la période"));
        ActivityMetrics.Add(new DashboardMetricViewModel(
            "Paiements fournisseurs",
            MoneyFormatter.FormatMinor(result.SupplierPaymentsMinor),
            "Flux sur la période"));
        ActivityMetrics.Add(new DashboardMetricViewModel(
            "Dépenses",
            MoneyFormatter.FormatMinor(result.ExpensesMinor),
            "Flux sur la période"));
        ActivityMetrics.Add(new DashboardMetricViewModel(
            "Résultat",
            MoneyFormatter.FormatMinor(result.ResultMinor),
            "Flux sur la période"));

        CurrentMetrics.Clear();
        CurrentMetrics.Add(new DashboardMetricViewModel(
            "Créances clients",
            MoneyFormatter.FormatMinor(result.CurrentCustomerReceivablesMinor),
            "État actuel"));
        CurrentMetrics.Add(new DashboardMetricViewModel(
            "Dettes fournisseurs",
            MoneyFormatter.FormatMinor(result.CurrentSupplierPayablesMinor),
            "État actuel"));
        CurrentMetrics.Add(new DashboardMetricViewModel(
            "Caisse",
            MoneyFormatter.FormatMinor(result.CurrentCashBalanceMinor),
            result.TreasuryIsInitialized ? "État actuel" : "Trésorerie non initialisée"));
        CurrentMetrics.Add(new DashboardMetricViewModel(
            "Banque",
            MoneyFormatter.FormatMinor(result.CurrentBankBalanceMinor),
            result.TreasuryIsInitialized ? "État actuel" : "Trésorerie non initialisée"));
        CurrentMetrics.Add(new DashboardMetricViewModel(
            "Trésorerie totale",
            MoneyFormatter.FormatMinor(result.CurrentTotalTreasuryMinor),
            result.TreasuryIsInitialized ? "État actuel" : "Non initialisée"));
    }

    private void ClearDashboardData()
    {
        DashboardData = null;
        HasData = false;
        ActivityMetrics.Clear();
        CurrentMetrics.Clear();
    }

    private void ClearDateValidationMessage()
    {
        if (ErrorMessage is InvalidDateRangeMessage or MissingDateMessage)
        {
            ErrorMessage = null;
        }
    }
}
