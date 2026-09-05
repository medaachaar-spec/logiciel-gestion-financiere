using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class ShellViewModel : ViewModelBase
{
    private ViewModelBase? _currentPage;
    private string _currentPageTitle = string.Empty;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly IAuthenticationService _authenticationService;
    private readonly RelayCommand _logoutRelayCommand;
    private readonly Lazy<DashboardViewModel> _dashboard;
    private readonly Lazy<TreasuryViewModel> _treasury;
    private readonly HashSet<NavigationItemViewModel> _authorizedItems = [];
    private bool _isInitialized;
    private bool _logoutRequested;

    public ShellViewModel(
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        IAuthenticationService authenticationService,
        Lazy<DashboardViewModel> dashboardViewModel,
        Lazy<TreasuryViewModel> treasuryViewModel)
    {
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(authenticationService);
        ArgumentNullException.ThrowIfNull(dashboardViewModel);
        ArgumentNullException.ThrowIfNull(treasuryViewModel);

        _currentUserSession = currentUserSession;
        _authorizationService = authorizationService;
        _authenticationService = authenticationService;
        _dashboard = dashboardViewModel;
        _treasury = treasuryViewModel;

        NavigateCommand = new RelayCommand(
            parameter =>
            {
                if (parameter is NavigationItemViewModel item
                    && NavigationItems.Contains(item)
                    && item.IsVisible)
                {
                    NavigateTo(item.PageKey);
                }
            },
            parameter => parameter is NavigationItemViewModel item
                && NavigationItems.Contains(item)
                && item.IsVisible
                && _isInitialized);

        _logoutRelayCommand = new RelayCommand(
            _ => RequestLogout(),
            _ => !_logoutRequested);
        LogoutCommand = _logoutRelayCommand;

        AddNavigationItem("Tableau de bord", "\uE80F", "dashboard", string.Empty, requiredCapability: ResponsibleCapability.GlobalDashboard);
        AddNavigationItem("Ventes", "\uE719", "sales", "Le contenu métier des ventes sera intégré dans une prochaine étape.", requiredModule: PermissionModule.Sales);
        AddNavigationItem("Achats", "\uE7BF", "purchases", "Le contenu métier des achats sera intégré dans une prochaine étape.", requiredModule: PermissionModule.Purchases);
        AddNavigationItem("Dépenses", "\uE7C1", "expenses", "Le contenu métier des dépenses sera intégré dans une prochaine étape.", requiredModule: PermissionModule.Expenses);
        AddNavigationItem("Clients", "\uE77B", "customers", "Le contenu métier des clients sera intégré dans une prochaine étape.", requiredModule: PermissionModule.Clients);
        AddNavigationItem("Fournisseurs", "\uE716", "suppliers", "Le contenu métier des fournisseurs sera intégré dans une prochaine étape.", requiredModule: PermissionModule.Suppliers);
        AddNavigationItem("Encaissements clients", "\uE8C7", "customer-payments", "Le contenu métier des encaissements clients sera intégré dans une prochaine étape.", requiredModule: PermissionModule.CustomerPayments);
        AddNavigationItem("Paiements fournisseurs", "\uE8C7", "supplier-payments", "Le contenu métier des paiements fournisseurs sera intégré dans une prochaine étape.", requiredModule: PermissionModule.SupplierPayments);
        AddNavigationItem("Caisse & Banque", "\uE8A1", "treasury", "Le contenu métier de la caisse et de la banque sera intégré dans une prochaine étape.", requiredCapability: ResponsibleCapability.Treasury);
        AddNavigationItem("Administration", "\uE713", "administration", "Le contenu métier de l'administration sera intégré dans une prochaine étape.", requiredCapability: ResponsibleCapability.Administration);
    }

    public ObservableCollection<NavigationItemViewModel> NavigationItems { get; } = [];

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

        if (!_currentUserSession.IsAuthenticated)
        {
            SetNoAuthorizedModulePage();
            _isInitialized = true;
            return false;
        }

        try
        {
            foreach (var item in NavigationItems)
            {
                var isAuthorized = item.RequiredModule is PermissionModule module
                    ? await _authorizationService.CanAsync(module, PermissionAction.View, cancellationToken)
                    : item.RequiredCapability is ResponsibleCapability capability
                        && _authorizationService.CanAccessResponsibleCapability(capability);

                item.IsVisible = isAuthorized;
                if (isAuthorized)
                {
                    _authorizedItems.Add(item);
                }
            }
        }
        catch (Exception exception)
        {
            Trace.TraceError("Authorization loading failed: {0}", exception);
            SetAllItemsHidden();
            CurrentPage = new PlaceholderPageViewModel(
                "Autorisations indisponibles",
                "Les autorisations ne sont pas disponibles actuellement.");
            CurrentPageTitle = "Autorisations indisponibles";
            _isInitialized = true;
            return true;
        }

        var firstVisibleItem = NavigationItems.FirstOrDefault(item => item.IsVisible);
        if (firstVisibleItem is null)
        {
            SetNoAuthorizedModulePage();
        }
        else
        {
            NavigateTo(firstVisibleItem);
        }

        _isInitialized = true;
        _logoutRelayCommand.RaiseCanExecuteChanged();
        return true;
    }

    public bool NavigateTo(string pageKey)
    {
        if (!_isInitialized || _logoutRequested || !_currentUserSession.IsAuthenticated
            || string.IsNullOrWhiteSpace(pageKey))
        {
            return false;
        }

        var item = NavigationItems.FirstOrDefault(navigationItem => navigationItem.PageKey == pageKey);
        if (item is null || !item.IsVisible || !_authorizedItems.Contains(item))
        {
            return false;
        }

        NavigateTo(item);
        return true;
    }

    private void AddNavigationItem(
        string title,
        string iconGlyph,
        string pageKey,
        string description,
        ViewModelBase? page = null,
        PermissionModule? requiredModule = null,
        ResponsibleCapability? requiredCapability = null)
    {
        page ??= new PlaceholderPageViewModel(title, description);
        NavigationItems.Add(new NavigationItemViewModel(
            title,
            iconGlyph,
            pageKey,
            page,
            NavigateCommand,
            requiredModule,
            requiredCapability));
    }

    private void NavigateTo(NavigationItemViewModel item)
    {
        if (_logoutRequested || !_currentUserSession.IsAuthenticated
            || !NavigationItems.Contains(item) || !item.IsVisible || !_authorizedItems.Contains(item))
        {
            return;
        }

        foreach (var navigationItem in NavigationItems)
        {
            navigationItem.IsSelected = ReferenceEquals(navigationItem, item);
        }

        CurrentPage = item.PageKey switch
        {
            "dashboard" => _dashboard.Value,
            "treasury" => _treasury.Value,
            _ => item.Page
        };
        CurrentPageTitle = item.Title;
    }

    private void RequestLogout()
    {
        if (_logoutRequested)
        {
            return;
        }

        _logoutRequested = true;
        Stop();
        _authenticationService.Logout();
        _logoutRelayCommand.RaiseCanExecuteChanged();
        LogoutRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SetAllItemsHidden()
    {
        _authorizedItems.Clear();
        foreach (var item in NavigationItems)
        {
            item.IsVisible = false;
            item.IsSelected = false;
        }
    }

    public void Stop()
    {
        _logoutRequested = true;
        if (_dashboard.IsValueCreated)
        {
            _dashboard.Value.Stop();
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
        var treasuryStop = _treasury.IsValueCreated
            ? _treasury.Value.StopAsync()
            : Task.CompletedTask;
        await Task.WhenAll(dashboardStop, treasuryStop);
    }

    private void SetNoAuthorizedModulePage()
    {
        CurrentPage = new PlaceholderPageViewModel(
            "Aucun module autorisé",
            "Aucun module n'est actuellement autorisé pour ce compte.");
        CurrentPageTitle = "Aucun module autorisé";
    }
}
