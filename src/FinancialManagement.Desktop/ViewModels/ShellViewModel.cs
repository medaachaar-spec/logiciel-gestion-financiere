using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private ViewModelBase? _currentPage;
    private string _currentPageTitle = string.Empty;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly IAuthenticationService _authenticationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly AsyncRelayCommand _navigateCommand;
    private readonly RelayCommand _logoutRelayCommand;
    private readonly ObservableCollection<NavigationItemViewModel> _navigationItems = [];
    private readonly Lazy<DashboardViewModel> _dashboard;
    private readonly Lazy<SalesViewModel> _sales;
    private readonly Lazy<PurchasesViewModel> _purchases;
    private readonly Lazy<ExpensesViewModel> _expenses;
    private readonly Lazy<ClientsViewModel> _clients;
    private readonly Lazy<SuppliersViewModel> _suppliers;
    private readonly Lazy<CustomerPaymentsViewModel> _customerPayments;
    private readonly Lazy<SupplierPaymentsViewModel> _supplierPayments;
    private readonly Lazy<TreasuryViewModel> _treasury;
    private readonly CancellationTokenSource _navigationCancellation = new();
    private long _navigationRequestVersion;
    private bool _isInitialized;
    private bool _logoutRequested;

    public ShellViewModel(
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        IAuthenticationService authenticationService,
        DashboardOperationCoordinator operationCoordinator,
        Lazy<DashboardViewModel> dashboardViewModel,
        Lazy<SalesViewModel> salesViewModel,
        Lazy<PurchasesViewModel> purchasesViewModel,
        Lazy<ExpensesViewModel> expensesViewModel,
        Lazy<ClientsViewModel> clientsViewModel,
        Lazy<SuppliersViewModel> suppliersViewModel,
        Lazy<CustomerPaymentsViewModel> customerPaymentsViewModel,
        Lazy<SupplierPaymentsViewModel> supplierPaymentsViewModel,
        Lazy<TreasuryViewModel> treasuryViewModel)
    {
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(authenticationService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);
        ArgumentNullException.ThrowIfNull(dashboardViewModel);
        ArgumentNullException.ThrowIfNull(salesViewModel);
        ArgumentNullException.ThrowIfNull(purchasesViewModel);
        ArgumentNullException.ThrowIfNull(expensesViewModel);
        ArgumentNullException.ThrowIfNull(clientsViewModel);
        ArgumentNullException.ThrowIfNull(suppliersViewModel);
        ArgumentNullException.ThrowIfNull(customerPaymentsViewModel);
        ArgumentNullException.ThrowIfNull(supplierPaymentsViewModel);
        ArgumentNullException.ThrowIfNull(treasuryViewModel);

        _currentUserSession = currentUserSession;
        _authorizationService = authorizationService;
        _authenticationService = authenticationService;
        _operationCoordinator = operationCoordinator;
        _dashboard = dashboardViewModel;
        _sales = salesViewModel;
        _purchases = purchasesViewModel;
        _expenses = expensesViewModel;
        _clients = clientsViewModel;
        _suppliers = suppliersViewModel;
        _customerPayments = customerPaymentsViewModel;
        _supplierPayments = supplierPaymentsViewModel;
        _treasury = treasuryViewModel;
        NavigationItems = new ReadOnlyObservableCollection<NavigationItemViewModel>(
            _navigationItems);

        _navigateCommand = new AsyncRelayCommand(
            async parameter =>
            {
                if (parameter is NavigationItemViewModel item
                    && NavigationItems.Contains(item))
                {
                    await NavigateToAsync(item.PageKey);
                }
            },
            parameter => parameter is NavigationItemViewModel item
                && NavigationItems.Contains(item)
                && _isInitialized
                && !_logoutRequested
                && _currentUserSession.IsAuthenticated);
        NavigateCommand = _navigateCommand;

        _logoutRelayCommand = new RelayCommand(
            _ => RequestLogout(),
            _ => !_logoutRequested);
        LogoutCommand = _logoutRelayCommand;

        AddNavigationItem(
            "Tableau de bord",
            "\uE80F",
            "dashboard",
            string.Empty,
            requiredCapability: ResponsibleCapability.GlobalDashboard,
            pageFactory: () => _dashboard.Value);
        AddNavigationItem(
            "Ventes",
            "\uE719",
            "sales",
            string.Empty,
            requiredModule: PermissionModule.Sales,
            pageFactory: () => _sales.Value);
        AddNavigationItem(
            "Achats",
            "\uE7BF",
            "purchases",
            string.Empty,
            requiredModule: PermissionModule.Purchases,
            pageFactory: () => _purchases.Value);
        AddNavigationItem(
            "Dépenses",
            "\uE7C1",
            "expenses",
            string.Empty,
            requiredModule: PermissionModule.Expenses,
            pageFactory: () => _expenses.Value);
        AddNavigationItem(
            "Clients",
            "\uE77B",
            "customers",
            string.Empty,
            requiredModule: PermissionModule.Clients,
            pageFactory: () => _clients.Value);
        AddNavigationItem(
            "Fournisseurs",
            "\uE716",
            "suppliers",
            string.Empty,
            requiredModule: PermissionModule.Suppliers,
            pageFactory: () => _suppliers.Value);
        AddNavigationItem(
            "Encaissements clients",
            "\uE8C7",
            "customer-payments",
            string.Empty,
            requiredModule: PermissionModule.CustomerPayments,
            pageFactory: () => _customerPayments.Value);
        AddNavigationItem(
            "Paiements fournisseurs",
            "\uE8C7",
            "supplier-payments",
            string.Empty,
            requiredModule: PermissionModule.SupplierPayments,
            pageFactory: () => _supplierPayments.Value);
        AddNavigationItem(
            "Caisse & Banque",
            "\uE8A1",
            "treasury",
            "Le contenu métier de la caisse et de la banque sera intégré dans une prochaine étape.",
            requiredCapability: ResponsibleCapability.Treasury,
            pageFactory: () => _treasury.Value);
        AddNavigationItem("Administration", "\uE713", "administration", "Le contenu métier de l'administration sera intégré dans une prochaine étape.", requiredCapability: ResponsibleCapability.Administration);
    }

    public ReadOnlyObservableCollection<NavigationItemViewModel> NavigationItems { get; }

    public ICommand NavigateCommand { get; }

    public ICommand LogoutCommand { get; }

    public event EventHandler? LogoutRequested;

    public string AuthenticatedUsername => _currentUserSession.Username ?? string.Empty;

    public ViewModelBase? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string CurrentPageTitle
    {
        get => _currentPageTitle;
        private set => SetProperty(ref _currentPageTitle, value);
    }

    public async Task<bool> InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return true;
        }

        SetAllItemsHidden();

        if (!TryCaptureAuthenticatedSession(out var sessionIdentity))
        {
            SetNoAuthorizedModulePage();
            _isInitialized = true;
            return false;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _navigationCancellation.Token);

        foreach (var item in NavigationItems)
        {
            var isAuthorized = await CanNavigateToRouteAsync(
                item,
                linkedCancellation.Token);

            if (linkedCancellation.IsCancellationRequested
                || !IsSameAuthenticatedSession(sessionIdentity))
            {
                SetAllItemsHidden();
                SetNoAuthorizedModulePage();
                _isInitialized = true;
                _navigateCommand.RaiseCanExecuteChanged();
                return false;
            }

            item.SetVisibility(isAuthorized);
        }

        _isInitialized = true;
        _navigateCommand.RaiseCanExecuteChanged();

        foreach (var item in NavigationItems.Where(item => item.IsVisible))
        {
            if (await NavigateToAsync(item.PageKey, linkedCancellation.Token))
            {
                _logoutRelayCommand.RaiseCanExecuteChanged();
                return true;
            }
        }

        SetNoAuthorizedModulePage();
        _logoutRelayCommand.RaiseCanExecuteChanged();
        return true;
    }

    public async Task<bool> NavigateToAsync(
        string? pageKey,
        CancellationToken cancellationToken = default)
    {
        if (!_isInitialized || _logoutRequested || string.IsNullOrWhiteSpace(pageKey))
        {
            return false;
        }

        var item = NavigationItems.FirstOrDefault(
            navigationItem => string.Equals(
                navigationItem.PageKey,
                pageKey,
                StringComparison.Ordinal));
        if (item is null || !TryCaptureAuthenticatedSession(out var sessionIdentity))
        {
            return false;
        }

        var requestVersion = Interlocked.Increment(ref _navigationRequestVersion);
        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _navigationCancellation.Token);

        var isAuthorized = await CanNavigateToRouteAsync(item, linkedCancellation.Token);
        if (!isAuthorized
            || linkedCancellation.IsCancellationRequested
            || requestVersion != Volatile.Read(ref _navigationRequestVersion)
            || !IsSameAuthenticatedSession(sessionIdentity))
        {
            return false;
        }

        ViewModelBase page;
        try
        {
            page = item.ResolvePage();
        }
        catch (Exception exception)
        {
            Trace.TraceError("Navigation page resolution failed for route {0}: {1}", pageKey, exception);
            return false;
        }

        if (linkedCancellation.IsCancellationRequested
            || requestVersion != Volatile.Read(ref _navigationRequestVersion)
            || !IsSameAuthenticatedSession(sessionIdentity))
        {
            return false;
        }

        foreach (var navigationItem in NavigationItems)
        {
            navigationItem.SetSelected(ReferenceEquals(navigationItem, item));
        }

        CurrentPage = page;
        CurrentPageTitle = item.Title;
        return true;
    }

    private void AddNavigationItem(
        string title,
        string iconGlyph,
        string pageKey,
        string description,
        PermissionModule? requiredModule = null,
        ResponsibleCapability? requiredCapability = null,
        Func<ViewModelBase>? pageFactory = null)
    {
        pageFactory ??= () => new PlaceholderPageViewModel(title, description);
        _navigationItems.Add(new NavigationItemViewModel(
            title,
            iconGlyph,
            pageKey,
            pageFactory,
            NavigateCommand,
            requiredModule,
            requiredCapability));
    }

    private async Task<bool> CanNavigateToRouteAsync(
        NavigationItemViewModel item,
        CancellationToken cancellationToken)
    {
        if (_logoutRequested
            || !NavigationItems.Contains(item)
            || !TryCaptureAuthenticatedSession(out _))
        {
            return false;
        }

        try
        {
            if (item.RequiredModule is PermissionModule module
                && item.RequiredAction is PermissionAction action
                && item.RequiredCapability is null)
            {
                return await _operationCoordinator.RunAsync(
                    token => _authorizationService.CanAsync(module, action, token),
                    cancellationToken);
            }

            return item.RequiredModule is null
                && item.RequiredAction is null
                && item.RequiredCapability is ResponsibleCapability capability
                && _authorizationService.CanAccessResponsibleCapability(capability);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            Trace.TraceError(
                "Navigation authorization failed for route {0}: {1}",
                item.PageKey,
                exception);
            return false;
        }
    }

    private void RequestLogout()
    {
        if (_logoutRequested)
        {
            return;
        }

        _logoutRequested = true;
        Interlocked.Increment(ref _navigationRequestVersion);
        _navigationCancellation.Cancel();
        Stop();
        _authenticationService.Logout();
        _logoutRelayCommand.RaiseCanExecuteChanged();
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetAllItemsHidden()
    {
        foreach (var item in NavigationItems)
        {
            item.SetVisibility(false);
            item.SetSelected(false);
        }
    }

    public void Stop()
    {
        _logoutRequested = true;
        Interlocked.Increment(ref _navigationRequestVersion);
        _navigationCancellation.Cancel();
        _navigateCommand.RaiseCanExecuteChanged();
        if (_dashboard.IsValueCreated)
        {
            _dashboard.Value.Stop();
        }

        if (_sales.IsValueCreated)
        {
            _sales.Value.Stop();
        }

        if (_purchases.IsValueCreated)
        {
            _purchases.Value.Stop();
        }

        if (_expenses.IsValueCreated)
        {
            _expenses.Value.Stop();
        }

        if (_clients.IsValueCreated)
        {
            _clients.Value.Stop();
        }

        if (_suppliers.IsValueCreated)
        {
            _suppliers.Value.Stop();
        }

        if (_customerPayments.IsValueCreated)
        {
            _customerPayments.Value.Stop();
        }

        if (_supplierPayments.IsValueCreated)
        {
            _supplierPayments.Value.Stop();
        }

        if (_treasury.IsValueCreated)
        {
            _treasury.Value.Stop();
        }

        SetAllItemsHidden();
        CurrentPage = null;
    }

    public async Task StopAsync()
    {
        Stop();
        var dashboardStop = _dashboard.IsValueCreated
            ? _dashboard.Value.StopAsync()
            : Task.CompletedTask;
        var salesStop = _sales.IsValueCreated
            ? _sales.Value.StopAsync()
            : Task.CompletedTask;
        var purchasesStop = _purchases.IsValueCreated
            ? _purchases.Value.StopAsync()
            : Task.CompletedTask;
        var expensesStop = _expenses.IsValueCreated
            ? _expenses.Value.StopAsync()
            : Task.CompletedTask;
        var clientsStop = _clients.IsValueCreated
            ? _clients.Value.StopAsync()
            : Task.CompletedTask;
        var suppliersStop = _suppliers.IsValueCreated
            ? _suppliers.Value.StopAsync()
            : Task.CompletedTask;
        var customerPaymentsStop = _customerPayments.IsValueCreated
            ? _customerPayments.Value.StopAsync()
            : Task.CompletedTask;
        var supplierPaymentsStop = _supplierPayments.IsValueCreated
            ? _supplierPayments.Value.StopAsync()
            : Task.CompletedTask;
        var treasuryStop = _treasury.IsValueCreated
            ? _treasury.Value.StopAsync()
            : Task.CompletedTask;
        await Task.WhenAll(
            dashboardStop,
            salesStop,
            purchasesStop,
            expensesStop,
            clientsStop,
            suppliersStop,
            customerPaymentsStop,
            supplierPaymentsStop,
            treasuryStop);
    }

    private void SetNoAuthorizedModulePage()
    {
        CurrentPage = new PlaceholderPageViewModel(
            "Aucun module autorisé",
            "Aucun module n'est actuellement autorisé pour ce compte.");
        CurrentPageTitle = "Aucun module autorisé";
    }

    private bool TryCaptureAuthenticatedSession(out SessionIdentity identity)
    {
        if (_currentUserSession.IsAuthenticated
            && _currentUserSession.UserId is long userId
            && _currentUserSession.Role is UserRole role)
        {
            identity = new SessionIdentity(userId, _currentUserSession.Username, role);
            return true;
        }

        identity = default;
        return false;
    }

    private bool IsSameAuthenticatedSession(SessionIdentity identity) =>
        _currentUserSession.IsAuthenticated
        && _currentUserSession.UserId == identity.UserId
        && string.Equals(_currentUserSession.Username, identity.Username, StringComparison.Ordinal)
        && _currentUserSession.Role == identity.Role;

    private readonly record struct SessionIdentity(
        long UserId,
        string? Username,
        UserRole Role);
}
