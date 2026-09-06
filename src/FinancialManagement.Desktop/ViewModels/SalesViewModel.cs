using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Clients;
using FinancialManagement.Application.Sales;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class SalesViewModel : ViewModelBase
{
    private const int PageSize = 25;
    private const int ClientLookupLimit = 20;
    private const string ListLoadError = "Impossible de charger les ventes.";
    private const string CreateError = "Impossible d'enregistrer la vente.";
    private const string RefreshAfterCreateError =
        "La vente a été enregistrée, mais l'actualisation de la liste a échoué.";
    private const string InvalidDateRangeMessage =
        "La date de début doit être antérieure ou égale à la date de fin.";
    private const string InvalidAmountMessage =
        "Saisissez un montant strictement positif avec au maximum deux décimales.";
    private const string InvalidInitialPaymentMessage =
        "Saisissez un paiement initial strictement positif avec au maximum deux décimales.";

    private readonly ISaleService _saleService;
    private readonly IClientLookupService _clientLookupService;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly AsyncRelayCommand _refreshRelayCommand;
    private readonly AsyncRelayCommand _applyFiltersRelayCommand;
    private readonly AsyncRelayCommand _resetFiltersRelayCommand;
    private readonly AsyncRelayCommand _previousPageRelayCommand;
    private readonly AsyncRelayCommand _nextPageRelayCommand;
    private readonly AsyncRelayCommand _searchClientsRelayCommand;
    private readonly AsyncRelayCommand _createSaleRelayCommand;
    private readonly RelayCommand _showCreateFormRelayCommand;
    private readonly RelayCommand _cancelCreateFormRelayCommand;
    private readonly RelayCommand _selectClientRelayCommand;
    private readonly RelayCommand _removeClientRelayCommand;
    private string _searchText = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private string? _appliedSearchText;
    private DateOnly? _appliedFromDate;
    private DateOnly? _appliedToDate;
    private int _pageNumber = 1;
    private int _totalPages;
    private int _totalCount;
    private bool _isLoading;
    private bool _isSaving;
    private bool _isLookingUpClients;
    private bool _hasLoadedList;
    private bool _permissionsLoaded;
    private bool _canViewSales;
    private bool _canCreateSales;
    private bool _canViewClients;
    private bool _isCreateFormVisible;
    private string? _listErrorMessage;
    private string? _formErrorMessage;
    private string? _lookupErrorMessage;
    private string? _successMessage;
    private string _totalAmount = string.Empty;
    private DateTime? _dueDate;
    private string _saleObservation = string.Empty;
    private bool _hasInitialPayment = true;
    private string _initialPaymentAmount = string.Empty;
    private string _paymentObservation = string.Empty;
    private PaymentMethodOptionViewModel? _selectedPaymentMethod;
    private string _clientSearchText = string.Empty;
    private ClientLookupItemViewModel? _selectedLookupResult;
    private ClientLookupItemViewModel? _selectedClient;
    private bool _initialLoadRequested;
    private bool _isStopped;
    private long _loadVersion;
    private CancellationTokenSource? _operationCancellation;
    private Task _pendingOperation = Task.CompletedTask;

    public SalesViewModel(
        ISaleService saleService,
        IClientLookupService clientLookupService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(saleService);
        ArgumentNullException.ThrowIfNull(clientLookupService);
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);

        _saleService = saleService;
        _clientLookupService = clientLookupService;
        _currentUserSession = currentUserSession;
        _authorizationService = authorizationService;
        _operationCoordinator = operationCoordinator;

        PaymentMethods =
        [
            new PaymentMethodOptionViewModel(PaymentMethod.Cash, "Espèces"),
            new PaymentMethodOptionViewModel(PaymentMethod.BankTransfer, "Virement bancaire")
        ];
        _selectedPaymentMethod = PaymentMethods[0];

        _refreshRelayCommand = new AsyncRelayCommand(
            _ => RefreshAsync(),
            _ => CanLoadPage);
        _applyFiltersRelayCommand = new AsyncRelayCommand(
            _ => ApplyFiltersAsync(),
            _ => CanLoadPage);
        _resetFiltersRelayCommand = new AsyncRelayCommand(
            _ => ResetFiltersAsync(),
            _ => CanLoadPage);
        _previousPageRelayCommand = new AsyncRelayCommand(
            _ => GoToPreviousPageAsync(),
            _ => CanLoadPage && PageNumber > 1);
        _nextPageRelayCommand = new AsyncRelayCommand(
            _ => GoToNextPageAsync(),
            _ => CanLoadPage && TotalPages > 0 && PageNumber < TotalPages);
        _searchClientsRelayCommand = new AsyncRelayCommand(
            _ => SearchClientsAsync(),
            _ => CanUseClientLookup && !IsBusy);
        _createSaleRelayCommand = new AsyncRelayCommand(
            _ => CreateSaleAsync(),
            _ => CanCreateSales && IsCreateFormVisible && !IsBusy && !_isStopped);
        _showCreateFormRelayCommand = new RelayCommand(
            _ => ShowCreateForm(),
            _ => CanCreateSales && !IsBusy && !IsCreateFormVisible && !_isStopped);
        _cancelCreateFormRelayCommand = new RelayCommand(
            _ => CancelCreateForm(),
            _ => IsCreateFormVisible && !IsBusy && !_isStopped);
        _selectClientRelayCommand = new RelayCommand(
            _ => SelectClient(),
            _ => SelectedLookupResult is not null && CanUseClientLookup && !IsBusy);
        _removeClientRelayCommand = new RelayCommand(
            _ => RemoveSelectedClient(),
            _ => SelectedClient is not null && IsCreateFormVisible && !IsBusy);

        RefreshCommand = _refreshRelayCommand;
        ApplyFiltersCommand = _applyFiltersRelayCommand;
        ResetFiltersCommand = _resetFiltersRelayCommand;
        PreviousPageCommand = _previousPageRelayCommand;
        NextPageCommand = _nextPageRelayCommand;
        SearchClientsCommand = _searchClientsRelayCommand;
        CreateSaleCommand = _createSaleRelayCommand;
        ShowCreateFormCommand = _showCreateFormRelayCommand;
        CancelCreateFormCommand = _cancelCreateFormRelayCommand;
        SelectClientCommand = _selectClientRelayCommand;
        RemoveClientCommand = _removeClientRelayCommand;
    }

    public ObservableCollection<SaleListItemViewModel> Sales { get; } = [];

    public ObservableCollection<ClientLookupItemViewModel> ClientResults { get; } = [];

    public IReadOnlyList<PaymentMethodOptionViewModel> PaymentMethods { get; }

    public ICommand RefreshCommand { get; }

    public ICommand ApplyFiltersCommand { get; }

    public ICommand ResetFiltersCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand SearchClientsCommand { get; }

    public ICommand CreateSaleCommand { get; }

    public ICommand ShowCreateFormCommand { get; }

    public ICommand CancelCreateFormCommand { get; }

    public ICommand SelectClientCommand { get; }

    public ICommand RemoveClientCommand { get; }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public DateTime? FromDate
    {
        get => _fromDate;
        set
        {
            if (SetProperty(ref _fromDate, value))
            {
                ClearDateValidationMessage();
            }
        }
    }

    public DateTime? ToDate
    {
        get => _toDate;
        set
        {
            if (SetProperty(ref _toDate, value))
            {
                ClearDateValidationMessage();
            }
        }
    }

    public int PageNumber
    {
        get => _pageNumber;
        private set
        {
            if (SetProperty(ref _pageNumber, value))
            {
                NotifyPaginationChanged();
            }
        }
    }

    public int TotalPages
    {
        get => _totalPages;
        private set
        {
            if (SetProperty(ref _totalPages, value))
            {
                NotifyPaginationChanged();
            }
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        private set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(TotalCountDescription));
            }
        }
    }

    public string PageDescription => TotalPages == 0
        ? "Page 0 / 0"
        : $"Page {PageNumber} / {TotalPages}";

    public string TotalCountDescription => TotalCount switch
    {
        0 => "Aucune vente",
        1 => "1 vente",
        _ => $"{TotalCount} ventes"
    };

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                NotifyBusyStateChanged();
            }
        }
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set
        {
            if (SetProperty(ref _isSaving, value))
            {
                NotifyBusyStateChanged();
            }
        }
    }

    public bool IsLookingUpClients
    {
        get => _isLookingUpClients;
        private set
        {
            if (SetProperty(ref _isLookingUpClients, value))
            {
                NotifyBusyStateChanged();
            }
        }
    }

    public bool IsBusy => IsLoading || IsSaving || IsLookingUpClients;

    public bool HasSales => Sales.Count > 0;

    public bool ShowEmptyState => HasLoadedList && !HasSales && !IsLoading;

    public string EmptyStateMessage => HasActiveFilters
        ? "Aucune vente ne correspond aux filtres."
        : "Aucune vente trouvée.";

    public bool HasLoadedList
    {
        get => _hasLoadedList;
        private set
        {
            if (SetProperty(ref _hasLoadedList, value))
            {
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    public bool HasActiveFilters => _appliedFromDate.HasValue
        || _appliedToDate.HasValue
        || !string.IsNullOrWhiteSpace(_appliedSearchText);

    public bool CanViewSales
    {
        get => _canViewSales;
        private set => SetProperty(ref _canViewSales, value);
    }

    public bool CanCreateSales
    {
        get => _canCreateSales;
        private set
        {
            if (SetProperty(ref _canCreateSales, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool CanViewClients
    {
        get => _canViewClients;
        private set
        {
            if (SetProperty(ref _canViewClients, value))
            {
                OnPropertyChanged(nameof(CanUseClientLookup));
                OnPropertyChanged(nameof(ShowClientLookupUnavailable));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool CanUseClientLookup => CanViewClients && IsCreateFormVisible;

    public bool ShowClientLookupUnavailable => _permissionsLoaded && !CanViewClients;

    public bool IsCreateFormVisible
    {
        get => _isCreateFormVisible;
        private set
        {
            if (SetProperty(ref _isCreateFormVisible, value))
            {
                OnPropertyChanged(nameof(CanUseClientLookup));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool HasListError => !string.IsNullOrWhiteSpace(ListErrorMessage);

    public string? ListErrorMessage
    {
        get => _listErrorMessage;
        private set
        {
            if (SetProperty(ref _listErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasListError));
            }
        }
    }

    public bool HasFormError => !string.IsNullOrWhiteSpace(FormErrorMessage);

    public string? FormErrorMessage
    {
        get => _formErrorMessage;
        private set
        {
            if (SetProperty(ref _formErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasFormError));
            }
        }
    }

    public bool HasLookupError => !string.IsNullOrWhiteSpace(LookupErrorMessage);

    public string? LookupErrorMessage
    {
        get => _lookupErrorMessage;
        private set
        {
            if (SetProperty(ref _lookupErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasLookupError));
            }
        }
    }

    public bool HasSuccessMessage => !string.IsNullOrWhiteSpace(SuccessMessage);

    public string? SuccessMessage
    {
        get => _successMessage;
        private set
        {
            if (SetProperty(ref _successMessage, value))
            {
                OnPropertyChanged(nameof(HasSuccessMessage));
            }
        }
    }

    public string TotalAmount
    {
        get => _totalAmount;
        set
        {
            if (SetProperty(ref _totalAmount, value))
            {
                if (IsAnonymousSale)
                {
                    InitialPaymentAmount = value;
                }

                ClearFormValidationMessage();
            }
        }
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    public string SaleObservation
    {
        get => _saleObservation;
        set => SetProperty(ref _saleObservation, value);
    }

    public bool HasInitialPayment
    {
        get => _hasInitialPayment;
        set
        {
            var effectiveValue = IsAnonymousSale || value;
            if (SetProperty(ref _hasInitialPayment, effectiveValue))
            {
                OnPropertyChanged(nameof(ShowInitialPaymentFields));
                ClearFormValidationMessage();
            }
        }
    }

    public bool ShowInitialPaymentFields => IsAnonymousSale || HasInitialPayment;

    public string InitialPaymentAmount
    {
        get => _initialPaymentAmount;
        set
        {
            if (SetProperty(ref _initialPaymentAmount, value))
            {
                ClearFormValidationMessage();
            }
        }
    }

    public string PaymentObservation
    {
        get => _paymentObservation;
        set => SetProperty(ref _paymentObservation, value);
    }

    public PaymentMethodOptionViewModel? SelectedPaymentMethod
    {
        get => _selectedPaymentMethod;
        set
        {
            if (SetProperty(ref _selectedPaymentMethod, value))
            {
                ClearFormValidationMessage();
            }
        }
    }

    public string ClientSearchText
    {
        get => _clientSearchText;
        set => SetProperty(ref _clientSearchText, value);
    }

    public ClientLookupItemViewModel? SelectedLookupResult
    {
        get => _selectedLookupResult;
        set
        {
            if (SetProperty(ref _selectedLookupResult, value))
            {
                _selectClientRelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public ClientLookupItemViewModel? SelectedClient
    {
        get => _selectedClient;
        private set
        {
            if (SetProperty(ref _selectedClient, value))
            {
                OnPropertyChanged(nameof(HasSelectedClient));
                OnPropertyChanged(nameof(IsAnonymousSale));
                OnPropertyChanged(nameof(ClientSelectionDescription));
                OnPropertyChanged(nameof(ShowInitialPaymentFields));
                if (value is null)
                {
                    HasInitialPayment = true;
                    InitialPaymentAmount = TotalAmount;
                }

                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedClient => SelectedClient is not null;

    public bool IsAnonymousSale => SelectedClient is null;

    public string ClientSelectionDescription => SelectedClient is null
        ? "Vente anonyme"
        : SelectedClient.DisplayLabel;

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_initialLoadRequested || _isStopped)
        {
            return _pendingOperation;
        }

        _initialLoadRequested = true;
        _pendingOperation = InitializeCoreAsync(cancellationToken);
        return _pendingOperation;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        StartPageLoad(PageNumber, cancellationToken);

    public Task ApplyFiltersAsync(CancellationToken cancellationToken = default)
    {
        if (!CanLoadPage)
        {
            return _pendingOperation;
        }

        if (FromDate is DateTime fromDate
            && ToDate is DateTime toDate
            && fromDate.Date > toDate.Date)
        {
            ListErrorMessage = InvalidDateRangeMessage;
            return Task.CompletedTask;
        }

        _appliedSearchText = string.IsNullOrWhiteSpace(SearchText) ? null : SearchText.Trim();
        _appliedFromDate = FromDate is DateTime from ? DateOnly.FromDateTime(from.Date) : null;
        _appliedToDate = ToDate is DateTime to ? DateOnly.FromDateTime(to.Date) : null;
        NotifyFilterStateChanged();
        return StartPageLoad(1, cancellationToken);
    }

    public Task ResetFiltersAsync(CancellationToken cancellationToken = default)
    {
        if (!CanLoadPage)
        {
            return _pendingOperation;
        }

        ResetFiltersWithoutLoading();
        return StartPageLoad(1, cancellationToken);
    }

    public Task GoToPreviousPageAsync(CancellationToken cancellationToken = default) =>
        PageNumber <= 1 ? Task.CompletedTask : StartPageLoad(PageNumber - 1, cancellationToken);

    public Task GoToNextPageAsync(CancellationToken cancellationToken = default) =>
        TotalPages == 0 || PageNumber >= TotalPages
            ? Task.CompletedTask
            : StartPageLoad(PageNumber + 1, cancellationToken);

    public Task SearchClientsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUseClientLookup || IsBusy || _isStopped)
        {
            return _pendingOperation;
        }

        _pendingOperation = SearchClientsCoreAsync(cancellationToken);
        return _pendingOperation;
    }

    public Task CreateSaleAsync(CancellationToken cancellationToken = default)
    {
        if (!CanCreateSales || !IsCreateFormVisible || IsBusy || _isStopped)
        {
            return _pendingOperation;
        }

        _pendingOperation = CreateSaleCoreAsync(cancellationToken);
        return _pendingOperation;
    }

    public void Stop()
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;
        Interlocked.Increment(ref _loadVersion);
        _operationCancellation?.Cancel();
        Sales.Clear();
        ClientResults.Clear();
        SelectedClient = null;
        SelectedLookupResult = null;
        HasLoadedList = false;
        IsCreateFormVisible = false;
        ListErrorMessage = null;
        FormErrorMessage = null;
        LookupErrorMessage = null;
        SuccessMessage = null;
        IsLoading = false;
        IsSaving = false;
        IsLookingUpClients = false;
        RaiseCommandCanExecuteChanged();
    }

    public Task StopAsync()
    {
        Stop();
        return _pendingOperation;
    }

    private bool CanLoadPage => CanViewSales && !IsBusy && !_isStopped;

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ListErrorMessage = null;
        var version = Interlocked.Increment(ref _loadVersion);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearSales();
                ListErrorMessage = ListLoadError;
                return;
            }

            var query = CreateListQuery(1);
            var payload = await _operationCoordinator.RunAsync(
                async token =>
                {
                    var salesPermissions = await _authorizationService.GetEffectivePermissionsAsync(
                        PermissionModule.Sales,
                        token);
                    var clientPermissions = await _authorizationService.GetEffectivePermissionsAsync(
                        PermissionModule.Clients,
                        token);
                    var result = salesPermissions.CanView
                        ? await _saleService.GetPageAsync(query, token)
                        : null;
                    return new InitializationPayload(salesPermissions, clientPermissions, result);
                },
                cancellation.Token);

            if (!CanApplyResult(identity, cancellation, version))
            {
                return;
            }

            _permissionsLoaded = true;
            CanViewSales = payload.SalesPermissions.CanView;
            CanCreateSales = payload.SalesPermissions.CanView && payload.SalesPermissions.CanCreate;
            CanViewClients = payload.ClientPermissions.CanView;
            OnPropertyChanged(nameof(ShowClientLookupUnavailable));

            if (!CanViewSales || payload.Result is null)
            {
                ClearSales();
                ListErrorMessage = "Vous n'êtes pas autorisé à consulter les ventes.";
                return;
            }

            ApplyListResult(payload.Result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the Shell closes.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Sales initialization failed: {0}", exception);
            if (!_isStopped)
            {
                ListErrorMessage = ListLoadError;
            }
        }
        finally
        {
            _operationCancellation = null;
            if (!_isStopped)
            {
                IsLoading = false;
            }
        }
    }

    private Task StartPageLoad(int pageNumber, CancellationToken cancellationToken)
    {
        if (!CanLoadPage)
        {
            return _pendingOperation;
        }

        _pendingOperation = LoadPageCoreAsync(pageNumber, cancellationToken);
        return _pendingOperation;
    }

    private async Task LoadPageCoreAsync(int pageNumber, CancellationToken cancellationToken)
    {
        IsLoading = true;
        ListErrorMessage = null;
        SuccessMessage = null;
        var version = Interlocked.Increment(ref _loadVersion);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearSales();
                ListErrorMessage = ListLoadError;
                return;
            }

            var result = await GetSalesPageAsync(pageNumber, cancellation.Token);
            if (!CanApplyResult(identity, cancellation, version))
            {
                return;
            }

            if (!result.Success)
            {
                HandleListFailure(result.FailureReason);
                return;
            }

            ApplyListResult(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the Shell closes.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Sales list loading failed: {0}", exception);
            if (!_isStopped)
            {
                ListErrorMessage = ListLoadError;
            }
        }
        finally
        {
            _operationCancellation = null;
            if (!_isStopped)
            {
                IsLoading = false;
            }
        }
    }

    private async Task SearchClientsCoreAsync(CancellationToken cancellationToken)
    {
        IsLookingUpClients = true;
        LookupErrorMessage = null;
        SelectedLookupResult = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity) || !CanViewClients)
            {
                return;
            }

            var query = new ClientLookupQuery(
                string.IsNullOrWhiteSpace(ClientSearchText) ? null : ClientSearchText.Trim(),
                ClientLookupLimit);
            var result = await _operationCoordinator.RunAsync(
                token => _clientLookupService.SearchAsync(query, token),
                cancellation.Token);

            if (!IsSameAuthenticatedSession(identity) || cancellation.IsCancellationRequested || _isStopped)
            {
                ClientResults.Clear();
                return;
            }

            if (!result.Success)
            {
                ClientResults.Clear();
                if (result.FailureReason is ClientLookupFailureReason.NotAuthenticated
                    or ClientLookupFailureReason.Forbidden)
                {
                    CanViewClients = false;
                    SelectedClient = null;
                    LookupErrorMessage = null;
                }
                else
                {
                    LookupErrorMessage = "Impossible de rechercher les clients.";
                }

                return;
            }

            ClientResults.Clear();
            foreach (var item in result.Items)
            {
                ClientResults.Add(new ClientLookupItemViewModel(item));
            }

            if (ClientResults.Count == 0)
            {
                LookupErrorMessage = "Aucun client trouvé.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the Shell closes.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Client lookup failed: {0}", exception);
            if (!_isStopped)
            {
                LookupErrorMessage = "Impossible de rechercher les clients.";
            }
        }
        finally
        {
            _operationCancellation = null;
            if (!_isStopped)
            {
                IsLookingUpClients = false;
            }
        }
    }

    private async Task CreateSaleCoreAsync(CancellationToken cancellationToken)
    {
        FormErrorMessage = null;
        SuccessMessage = null;

        if (!TryCreateSaleCommand(out var command))
        {
            return;
        }

        IsSaving = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity) || !CanCreateSales)
            {
                FormErrorMessage = "Vous n'êtes pas autorisé à créer une vente.";
                return;
            }

            var result = await _operationCoordinator.RunAsync(
                token => _saleService.CreateAsync(command, token),
                cancellation.Token);
            if (!IsSameAuthenticatedSession(identity) || cancellation.IsCancellationRequested || _isStopped)
            {
                return;
            }

            if (!result.Success)
            {
                if (result.FailureReason is CreateSaleFailureReason.NotAuthenticated
                    or CreateSaleFailureReason.NotAuthorized)
                {
                    CanCreateSales = false;
                }

                FormErrorMessage = MapCreateFailure(result.FailureReason);
                return;
            }

            ResetCreateForm();
            IsCreateFormVisible = false;
            ResetFiltersWithoutLoading();
            SuccessMessage = "Vente enregistrée.";

            var refreshResult = await GetSalesPageAsync(1, cancellation.Token);
            if (!IsSameAuthenticatedSession(identity) || cancellation.IsCancellationRequested || _isStopped)
            {
                return;
            }

            if (refreshResult.Success)
            {
                ApplyListResult(refreshResult);
            }
            else
            {
                HandleListFailure(refreshResult.FailureReason, preserveSuccess: true);
                ListErrorMessage = RefreshAfterCreateError;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the Shell closes.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Sale creation failed: {0}", exception);
            if (!_isStopped)
            {
                FormErrorMessage = CreateError;
            }
        }
        finally
        {
            _operationCancellation = null;
            if (!_isStopped)
            {
                IsSaving = false;
            }
        }
    }

    private bool TryCreateSaleCommand(out CreateSaleCommand command)
    {
        command = null!;

        if (!MoneyParser.TryParsePositiveMinorUnits(TotalAmount, out var totalAmountMinor))
        {
            FormErrorMessage = InvalidAmountMessage;
            return false;
        }

        InitialCustomerPaymentInput? initialPayment = null;
        if (ShowInitialPaymentFields)
        {
            if (!MoneyParser.TryParsePositiveMinorUnits(
                    InitialPaymentAmount,
                    out var initialPaymentMinor))
            {
                FormErrorMessage = InvalidInitialPaymentMessage;
                return false;
            }

            if (SelectedPaymentMethod is not { } paymentMethod)
            {
                FormErrorMessage = "Sélectionnez un mode de paiement.";
                return false;
            }

            if (IsAnonymousSale && initialPaymentMinor != totalAmountMinor)
            {
                FormErrorMessage =
                    "Pour une vente anonyme, le paiement initial doit être égal au montant total.";
                return false;
            }

            initialPayment = new InitialCustomerPaymentInput(
                initialPaymentMinor,
                paymentMethod.Value,
                NormalizeOptionalText(PaymentObservation));
        }

        command = new CreateSaleCommand(
            SelectedClient?.ClientId,
            totalAmountMinor,
            NormalizeOptionalText(SaleObservation),
            DueDate is DateTime dueDate ? DateOnly.FromDateTime(dueDate.Date) : null,
            initialPayment);
        return true;
    }

    private Task<SaleListResult> GetSalesPageAsync(
        int pageNumber,
        CancellationToken cancellationToken) =>
        _operationCoordinator.RunAsync(
            token => _saleService.GetPageAsync(CreateListQuery(pageNumber), token),
            cancellationToken);

    private SaleListQuery CreateListQuery(int pageNumber) => new(
        pageNumber,
        PageSize,
        _appliedFromDate,
        _appliedToDate,
        _appliedSearchText);

    private void ApplyListResult(SaleListResult result)
    {
        Sales.Clear();
        foreach (var item in result.Items)
        {
            Sales.Add(new SaleListItemViewModel(item));
        }

        PageNumber = result.PageNumber;
        TotalPages = result.TotalPages;
        TotalCount = result.TotalCount;
        HasLoadedList = true;
        ListErrorMessage = null;
        NotifySalesCollectionChanged();
    }

    private void HandleListFailure(
        SaleListFailureReason failureReason,
        bool preserveSuccess = false)
    {
        if (!preserveSuccess)
        {
            SuccessMessage = null;
        }

        if (failureReason is SaleListFailureReason.NotAuthenticated
            or SaleListFailureReason.Forbidden)
        {
            ClearSales();
            CanViewSales = false;
            CanCreateSales = false;
            ListErrorMessage = "Vous n'êtes pas autorisé à consulter les ventes.";
            return;
        }

        ListErrorMessage = failureReason switch
        {
            SaleListFailureReason.InvalidDateRange => InvalidDateRangeMessage,
            SaleListFailureReason.InvalidPageNumber => "La page demandée n'est pas disponible.",
            SaleListFailureReason.InvalidPageSize => "La taille de page demandée n'est pas valide.",
            _ => ListLoadError
        };
    }

    private void ClearSales()
    {
        Sales.Clear();
        PageNumber = 1;
        TotalPages = 0;
        TotalCount = 0;
        HasLoadedList = false;
        NotifySalesCollectionChanged();
    }

    private void ShowCreateForm()
    {
        ResetCreateForm();
        SuccessMessage = null;
        IsCreateFormVisible = true;
    }

    private void CancelCreateForm()
    {
        ResetCreateForm();
        IsCreateFormVisible = false;
    }

    private void ResetCreateForm()
    {
        TotalAmount = string.Empty;
        DueDate = null;
        SaleObservation = string.Empty;
        SelectedClient = null;
        ClientSearchText = string.Empty;
        SelectedLookupResult = null;
        ClientResults.Clear();
        HasInitialPayment = true;
        InitialPaymentAmount = string.Empty;
        SelectedPaymentMethod = PaymentMethods[0];
        PaymentObservation = string.Empty;
        FormErrorMessage = null;
        LookupErrorMessage = null;
    }

    private void SelectClient()
    {
        if (SelectedLookupResult is null || !CanUseClientLookup)
        {
            return;
        }

        SelectedClient = SelectedLookupResult;
        SelectedLookupResult = null;
        ClientResults.Clear();
        LookupErrorMessage = null;
    }

    private void RemoveSelectedClient()
    {
        SelectedClient = null;
        SelectedLookupResult = null;
        ClientResults.Clear();
        LookupErrorMessage = null;
    }

    private void ResetFiltersWithoutLoading()
    {
        SearchText = string.Empty;
        FromDate = null;
        ToDate = null;
        _appliedSearchText = null;
        _appliedFromDate = null;
        _appliedToDate = null;
        NotifyFilterStateChanged();
    }

    private bool CanApplyResult(
        SessionIdentity identity,
        CancellationTokenSource cancellation,
        long version) =>
        !_isStopped
        && !cancellation.IsCancellationRequested
        && version == Volatile.Read(ref _loadVersion)
        && IsSameAuthenticatedSession(identity);

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

    private static string MapCreateFailure(CreateSaleFailureReason failureReason) => failureReason switch
    {
        CreateSaleFailureReason.NotAuthenticated => "Votre session n'est plus active.",
        CreateSaleFailureReason.NotAuthorized => "Vous n'êtes pas autorisé à créer une vente.",
        CreateSaleFailureReason.InvalidTotalAmount => InvalidAmountMessage,
        CreateSaleFailureReason.ClientNotFound =>
            "Le client sélectionné n'est plus disponible. Recherchez-le de nouveau ou créez une vente anonyme.",
        CreateSaleFailureReason.AnonymousSaleRequiresInitialPayment =>
            "Une vente anonyme doit être entièrement payée lors de sa création.",
        CreateSaleFailureReason.AnonymousSalePaymentMustMatchTotal =>
            "Pour une vente anonyme, le paiement initial doit être égal au montant total.",
        CreateSaleFailureReason.InvalidInitialPaymentAmount => InvalidInitialPaymentMessage,
        CreateSaleFailureReason.InvalidPaymentMethod => "Le mode de paiement sélectionné n'est pas valide.",
        _ => CreateError
    };

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void ClearDateValidationMessage()
    {
        if (ListErrorMessage == InvalidDateRangeMessage)
        {
            ListErrorMessage = null;
        }
    }

    private void ClearFormValidationMessage()
    {
        if (FormErrorMessage is InvalidAmountMessage
            or InvalidInitialPaymentMessage
            or "Pour une vente anonyme, le paiement initial doit être égal au montant total."
            or "Sélectionnez un mode de paiement.")
        {
            FormErrorMessage = null;
        }
    }

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowEmptyState));
        RaiseCommandCanExecuteChanged();
    }

    private void NotifyPaginationChanged()
    {
        OnPropertyChanged(nameof(PageDescription));
        RaiseCommandCanExecuteChanged();
    }

    private void NotifySalesCollectionChanged()
    {
        OnPropertyChanged(nameof(HasSales));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void NotifyFilterStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private void RaiseCommandCanExecuteChanged()
    {
        _refreshRelayCommand?.RaiseCanExecuteChanged();
        _applyFiltersRelayCommand?.RaiseCanExecuteChanged();
        _resetFiltersRelayCommand?.RaiseCanExecuteChanged();
        _previousPageRelayCommand?.RaiseCanExecuteChanged();
        _nextPageRelayCommand?.RaiseCanExecuteChanged();
        _searchClientsRelayCommand?.RaiseCanExecuteChanged();
        _createSaleRelayCommand?.RaiseCanExecuteChanged();
        _showCreateFormRelayCommand?.RaiseCanExecuteChanged();
        _cancelCreateFormRelayCommand?.RaiseCanExecuteChanged();
        _selectClientRelayCommand?.RaiseCanExecuteChanged();
        _removeClientRelayCommand?.RaiseCanExecuteChanged();
    }

    private sealed record InitializationPayload(
        EffectiveModulePermission SalesPermissions,
        EffectiveModulePermission ClientPermissions,
        SaleListResult? Result);

    private readonly record struct SessionIdentity(
        long UserId,
        string? Username,
        UserRole Role);
}
