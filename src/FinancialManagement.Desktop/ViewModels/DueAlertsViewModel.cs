using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Alerts;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Desktop.Services;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class DueAlertsViewModel : ViewModelBase
{
    private const string GenericErrorMessage = "Impossible de charger les échéances.";

    private readonly IFinancialDueAlertService _dueAlertService;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly AsyncRelayCommand _refreshRelayCommand;
    private bool _isLoading;
    private bool _hasData;
    private bool _hasAlerts;
    private string? _errorMessage;
    private string _currentDateDescription = string.Empty;
    private int _totalCustomerAlertCount;
    private int _totalSupplierAlertCount;
    private int _overdueCustomerCount;
    private int _overdueSupplierCount;
    private bool _initialLoadRequested;
    private bool _isStopped;
    private CancellationTokenSource? _loadCancellation;
    private Task _pendingLoad = Task.CompletedTask;

    public DueAlertsViewModel(
        IFinancialDueAlertService dueAlertService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(dueAlertService);
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);

        _dueAlertService = dueAlertService;
        _currentUserSession = currentUserSession;
        _authorizationService = authorizationService;
        _operationCoordinator = operationCoordinator;
        _refreshRelayCommand = new AsyncRelayCommand(
            _ => RefreshAsync(),
            _ => !IsLoading && !_isStopped);
        RefreshCommand = _refreshRelayCommand;
    }

    public ObservableCollection<DueAlertItemViewModel> CustomerAlerts { get; } = [];

    public ObservableCollection<DueAlertItemViewModel> SupplierAlerts { get; } = [];

    public ICommand RefreshCommand { get; }

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

    public bool HasAlerts
    {
        get => _hasAlerts;
        private set => SetProperty(ref _hasAlerts, value);
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

    public string CurrentDateDescription
    {
        get => _currentDateDescription;
        private set => SetProperty(ref _currentDateDescription, value);
    }

    public int TotalCustomerAlertCount
    {
        get => _totalCustomerAlertCount;
        private set => SetProperty(ref _totalCustomerAlertCount, value);
    }

    public int TotalSupplierAlertCount
    {
        get => _totalSupplierAlertCount;
        private set => SetProperty(ref _totalSupplierAlertCount, value);
    }

    public int OverdueCustomerCount
    {
        get => _overdueCustomerCount;
        private set => SetProperty(ref _overdueCustomerCount, value);
    }

    public int OverdueSupplierCount
    {
        get => _overdueSupplierCount;
        private set => SetProperty(ref _overdueSupplierCount, value);
    }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_initialLoadRequested || _isStopped)
        {
            return _pendingLoad;
        }

        _initialLoadRequested = true;
        return StartLoad(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_isStopped || IsLoading)
        {
            return _pendingLoad;
        }

        _initialLoadRequested = true;
        return StartLoad(cancellationToken);
    }

    public void Stop()
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;
        _loadCancellation?.Cancel();
        ClearAlertData();
        ErrorMessage = null;
        IsLoading = false;
        _refreshRelayCommand.RaiseCanExecuteChanged();
    }

    public Task StopAsync()
    {
        Stop();
        return _pendingLoad;
    }

    private Task StartLoad(CancellationToken cancellationToken)
    {
        _pendingLoad = LoadCoreAsync(cancellationToken);
        return _pendingLoad;
    }

    private async Task LoadCoreAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ErrorMessage = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _loadCancellation = cancellation;

        try
        {
            if (!CanAccessAlerts())
            {
                ClearAlertData();
                ErrorMessage = GenericErrorMessage;
                return;
            }

            var userId = _currentUserSession.UserId;
            var result = await _operationCoordinator.RunAsync(
                token => _dueAlertService.GetAlertsAsync(new FinancialDueAlertsQuery(), token),
                cancellation.Token);

            if (_isStopped || cancellation.IsCancellationRequested)
            {
                return;
            }

            if (!CanAccessAlerts() || _currentUserSession.UserId != userId)
            {
                ClearAlertData();
                ErrorMessage = GenericErrorMessage;
                return;
            }

            if (!result.Success)
            {
                ClearAlertData();
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
            Trace.TraceError("Due-alert loading failed: {0}", exception);
            if (!_isStopped)
            {
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

    private bool CanAccessAlerts() =>
        _currentUserSession.IsAuthenticated
        && _authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.GlobalDashboard);

    private void ApplyResult(FinancialDueAlertsResult result)
    {
        CustomerAlerts.Clear();
        foreach (var alert in result.CustomerAlerts)
        {
            CustomerAlerts.Add(new DueAlertItemViewModel(alert));
        }

        SupplierAlerts.Clear();
        foreach (var alert in result.SupplierAlerts)
        {
            SupplierAlerts.Add(new DueAlertItemViewModel(alert));
        }

        TotalCustomerAlertCount = result.TotalCustomerAlertCount;
        TotalSupplierAlertCount = result.TotalSupplierAlertCount;
        OverdueCustomerCount = result.OverdueCustomerCount;
        OverdueSupplierCount = result.OverdueSupplierCount;
        HasAlerts = result.TotalCustomerAlertCount != 0 || result.TotalSupplierAlertCount != 0;
        CurrentDateDescription = result.CurrentDate is DateOnly currentDate
            ? $"Situation au {currentDate:dd/MM/yyyy}"
            : string.Empty;
        HasData = true;
        ErrorMessage = null;
    }

    private void ClearAlertData()
    {
        CustomerAlerts.Clear();
        SupplierAlerts.Clear();
        TotalCustomerAlertCount = 0;
        TotalSupplierAlertCount = 0;
        OverdueCustomerCount = 0;
        OverdueSupplierCount = 0;
        CurrentDateDescription = string.Empty;
        HasAlerts = false;
        HasData = false;
    }
}
