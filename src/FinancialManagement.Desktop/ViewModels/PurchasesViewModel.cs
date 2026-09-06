using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Purchases;
using FinancialManagement.Application.Suppliers;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class PurchasesViewModel : ViewModelBase
{
    private const int PageSize = 25;
    private const int SupplierLookupLimit = 20;
    private const string ListLoadError = "Impossible de charger les achats.";
    private const string CreateError = "Impossible d'enregistrer l'achat.";
    private const string RefreshAfterCreateError =
        "L'achat a été enregistré, mais l'actualisation de la liste a échoué.";
    private const string InvalidDateRangeMessage =
        "La date de début doit être antérieure ou égale à la date de fin.";
    private const string InvalidAmountMessage =
        "Saisissez un montant strictement positif avec au maximum deux décimales.";
    private const string InvalidInitialPaymentMessage =
        "Saisissez un paiement initial strictement positif avec au maximum deux décimales.";
    private const string SupplierRequiredMessage =
        "Sélectionnez un fournisseur avant d'enregistrer l'achat.";
    private const string SupplierPermissionMessage =
        "L'accès aux fournisseurs est requis pour créer un achat.";

    private readonly IPurchaseService _purchaseService;
    private readonly ISupplierLookupService _supplierLookupService;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly AsyncRelayCommand _refreshRelayCommand;
    private readonly AsyncRelayCommand _applyFiltersRelayCommand;
    private readonly AsyncRelayCommand _resetFiltersRelayCommand;
    private readonly AsyncRelayCommand _previousPageRelayCommand;
    private readonly AsyncRelayCommand _nextPageRelayCommand;
    private readonly AsyncRelayCommand _searchSuppliersRelayCommand;
    private readonly AsyncRelayCommand _createPurchaseRelayCommand;
    private readonly RelayCommand _showCreateFormRelayCommand;
    private readonly RelayCommand _cancelCreateFormRelayCommand;
    private readonly RelayCommand _selectSupplierRelayCommand;
    private readonly RelayCommand _removeSupplierRelayCommand;
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
    private bool _isLookingUpSuppliers;
    private bool _hasLoadedList;
    private bool _permissionsLoaded;
    private bool _canViewPurchases;
    private bool _canCreatePurchases;
    private bool _canViewSuppliers;
    private bool _isCreateFormVisible;
    private string? _listErrorMessage;
    private string? _formErrorMessage;
    private string? _lookupErrorMessage;
    private string? _successMessage;
    private string _totalAmount = string.Empty;
    private DateTime? _dueDate;
    private string _purchaseObservation = string.Empty;
    private bool _hasInitialPayment;
    private string _initialPaymentAmount = string.Empty;
    private string _paymentObservation = string.Empty;
    private PaymentMethodOptionViewModel? _selectedPaymentMethod;
    private string _supplierSearchText = string.Empty;
    private SupplierLookupItemViewModel? _selectedLookupResult;
    private SupplierLookupItemViewModel? _selectedSupplier;
    private bool _initialLoadRequested;
    private bool _isStopped;
    private long _loadVersion;
    private CancellationTokenSource? _operationCancellation;
    private Task _pendingOperation = Task.CompletedTask;

    public PurchasesViewModel(
        IPurchaseService purchaseService,
        ISupplierLookupService supplierLookupService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(purchaseService);
        ArgumentNullException.ThrowIfNull(supplierLookupService);
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);

        _purchaseService = purchaseService;
        _supplierLookupService = supplierLookupService;
        _currentUserSession = currentUserSession;
        _authorizationService = authorizationService;
        _operationCoordinator = operationCoordinator;

        PaymentMethods =
        [
            new PaymentMethodOptionViewModel(PaymentMethod.Cash, "Espèces"),
            new PaymentMethodOptionViewModel(PaymentMethod.BankTransfer, "Virement bancaire")
        ];
        _selectedPaymentMethod = PaymentMethods[0];

        _refreshRelayCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => CanLoadPage);
        _applyFiltersRelayCommand = new AsyncRelayCommand(_ => ApplyFiltersAsync(), _ => CanLoadPage);
        _resetFiltersRelayCommand = new AsyncRelayCommand(_ => ResetFiltersAsync(), _ => CanLoadPage);
        _previousPageRelayCommand = new AsyncRelayCommand(
            _ => GoToPreviousPageAsync(),
            _ => CanLoadPage && PageNumber > 1);
        _nextPageRelayCommand = new AsyncRelayCommand(
            _ => GoToNextPageAsync(),
            _ => CanLoadPage && TotalPages > 0 && PageNumber < TotalPages);
        _searchSuppliersRelayCommand = new AsyncRelayCommand(
            _ => SearchSuppliersAsync(),
            _ => CanUseSupplierLookup && !IsBusy);
        _createPurchaseRelayCommand = new AsyncRelayCommand(
            _ => CreatePurchaseAsync(),
            _ => CanStartPurchaseCreation && IsCreateFormVisible && !IsBusy && !_isStopped);
        _showCreateFormRelayCommand = new RelayCommand(
            _ => ShowCreateForm(),
            _ => CanStartPurchaseCreation && !IsBusy && !IsCreateFormVisible && !_isStopped);
        _cancelCreateFormRelayCommand = new RelayCommand(
            _ => CancelCreateForm(),
            _ => IsCreateFormVisible && !IsBusy && !_isStopped);
        _selectSupplierRelayCommand = new RelayCommand(
            _ => SelectSupplier(),
            _ => SelectedLookupResult is not null && CanUseSupplierLookup && !IsBusy);
        _removeSupplierRelayCommand = new RelayCommand(
            _ => RemoveSelectedSupplier(),
            _ => SelectedSupplier is not null && IsCreateFormVisible && !IsBusy);

        RefreshCommand = _refreshRelayCommand;
        ApplyFiltersCommand = _applyFiltersRelayCommand;
        ResetFiltersCommand = _resetFiltersRelayCommand;
        PreviousPageCommand = _previousPageRelayCommand;
        NextPageCommand = _nextPageRelayCommand;
        SearchSuppliersCommand = _searchSuppliersRelayCommand;
        CreatePurchaseCommand = _createPurchaseRelayCommand;
        ShowCreateFormCommand = _showCreateFormRelayCommand;
        CancelCreateFormCommand = _cancelCreateFormRelayCommand;
        SelectSupplierCommand = _selectSupplierRelayCommand;
        RemoveSupplierCommand = _removeSupplierRelayCommand;
    }

    public ObservableCollection<PurchaseListItemViewModel> Purchases { get; } = [];

    public ObservableCollection<SupplierLookupItemViewModel> SupplierResults { get; } = [];

    public IReadOnlyList<PaymentMethodOptionViewModel> PaymentMethods { get; }

    public ICommand RefreshCommand { get; }
    public ICommand ApplyFiltersCommand { get; }
    public ICommand ResetFiltersCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand SearchSuppliersCommand { get; }
    public ICommand CreatePurchaseCommand { get; }
    public ICommand ShowCreateFormCommand { get; }
    public ICommand CancelCreateFormCommand { get; }
    public ICommand SelectSupplierCommand { get; }
    public ICommand RemoveSupplierCommand { get; }

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

    public string PageDescription => TotalPages == 0 ? "Page 0 / 0" : $"Page {PageNumber} / {TotalPages}";

    public string TotalCountDescription => TotalCount switch
    {
        0 => "Aucun achat",
        1 => "1 achat",
        _ => $"{TotalCount} achats"
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

    public bool IsLookingUpSuppliers
    {
        get => _isLookingUpSuppliers;
        private set
        {
            if (SetProperty(ref _isLookingUpSuppliers, value))
            {
                NotifyBusyStateChanged();
            }
        }
    }

    public bool IsBusy => IsLoading || IsSaving || IsLookingUpSuppliers;
    public bool HasPurchases => Purchases.Count > 0;
    public bool ShowEmptyState => HasLoadedList && !HasPurchases && !IsLoading;
    public string EmptyStateMessage => HasActiveFilters
        ? "Aucun achat ne correspond aux filtres."
        : "Aucun achat trouvé.";

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

    public bool CanViewPurchases
    {
        get => _canViewPurchases;
        private set => SetProperty(ref _canViewPurchases, value);
    }

    public bool CanCreatePurchases
    {
        get => _canCreatePurchases;
        private set
        {
            if (SetProperty(ref _canCreatePurchases, value))
            {
                NotifyCreationPermissionChanged();
            }
        }
    }

    public bool CanViewSuppliers
    {
        get => _canViewSuppliers;
        private set
        {
            if (SetProperty(ref _canViewSuppliers, value))
            {
                NotifyCreationPermissionChanged();
                OnPropertyChanged(nameof(CanUseSupplierLookup));
                OnPropertyChanged(nameof(ShowSupplierAccessRequired));
            }
        }
    }

    public bool CanStartPurchaseCreation => CanCreatePurchases && CanViewSuppliers;
    public bool CanUseSupplierLookup => CanViewSuppliers && IsCreateFormVisible;
    public bool ShowSupplierAccessRequired =>
        _permissionsLoaded && CanCreatePurchases && !CanViewSuppliers;

    public bool IsCreateFormVisible
    {
        get => _isCreateFormVisible;
        private set
        {
            if (SetProperty(ref _isCreateFormVisible, value))
            {
                OnPropertyChanged(nameof(CanUseSupplierLookup));
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
                ClearFormValidationMessage();
            }
        }
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    public string PurchaseObservation
    {
        get => _purchaseObservation;
        set => SetProperty(ref _purchaseObservation, value);
    }

    public bool HasInitialPayment
    {
        get => _hasInitialPayment;
        set
        {
            if (SetProperty(ref _hasInitialPayment, value))
            {
                OnPropertyChanged(nameof(ShowInitialPaymentFields));
                ClearFormValidationMessage();
            }
        }
    }

    public bool ShowInitialPaymentFields => HasInitialPayment;

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

    public string SupplierSearchText
    {
        get => _supplierSearchText;
        set => SetProperty(ref _supplierSearchText, value);
    }

    public SupplierLookupItemViewModel? SelectedLookupResult
    {
        get => _selectedLookupResult;
        set
        {
            if (SetProperty(ref _selectedLookupResult, value))
            {
                _selectSupplierRelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public SupplierLookupItemViewModel? SelectedSupplier
    {
        get => _selectedSupplier;
        private set
        {
            if (SetProperty(ref _selectedSupplier, value))
            {
                OnPropertyChanged(nameof(HasSelectedSupplier));
                OnPropertyChanged(nameof(SupplierSelectionDescription));
                ClearFormValidationMessage();
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedSupplier => SelectedSupplier is not null;
    public string SupplierSelectionDescription => SelectedSupplier?.DisplayLabel ?? "Aucun fournisseur sélectionné";

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

    public Task SearchSuppliersAsync(CancellationToken cancellationToken = default)
    {
        if (!CanUseSupplierLookup || IsBusy || _isStopped)
        {
            return _pendingOperation;
        }

        _pendingOperation = SearchSuppliersCoreAsync(cancellationToken);
        return _pendingOperation;
    }

    public Task CreatePurchaseAsync(CancellationToken cancellationToken = default)
    {
        if (!CanStartPurchaseCreation || !IsCreateFormVisible || IsBusy || _isStopped)
        {
            return _pendingOperation;
        }

        _pendingOperation = CreatePurchaseCoreAsync(cancellationToken);
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
        Purchases.Clear();
        SupplierResults.Clear();
        SelectedSupplier = null;
        SelectedLookupResult = null;
        HasLoadedList = false;
        IsCreateFormVisible = false;
        ListErrorMessage = null;
        FormErrorMessage = null;
        LookupErrorMessage = null;
        SuccessMessage = null;
        IsLoading = false;
        IsSaving = false;
        IsLookingUpSuppliers = false;
        RaiseCommandCanExecuteChanged();
    }

    public Task StopAsync()
    {
        Stop();
        return _pendingOperation;
    }

    private bool CanLoadPage => CanViewPurchases && !IsBusy && !_isStopped;

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
                ClearSensitiveState();
                return;
            }

            var query = CreateListQuery(1);
            var payload = await _operationCoordinator.RunAsync(
                async token =>
                {
                    var purchasePermissions = await _authorizationService.GetEffectivePermissionsAsync(
                        PermissionModule.Purchases,
                        token);
                    var supplierPermissions = await _authorizationService.GetEffectivePermissionsAsync(
                        PermissionModule.Suppliers,
                        token);
                    var result = purchasePermissions.CanView
                        ? await _purchaseService.GetPageAsync(query, token)
                        : null;
                    return new InitializationPayload(purchasePermissions, supplierPermissions, result);
                },
                cancellation.Token);

            if (!CanApplyResult(identity, cancellation, version))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            _permissionsLoaded = true;
            CanViewPurchases = payload.PurchasePermissions.CanView;
            CanCreatePurchases = payload.PurchasePermissions.CanView
                && payload.PurchasePermissions.CanCreate;
            CanViewSuppliers = payload.SupplierPermissions.CanView;
            OnPropertyChanged(nameof(ShowSupplierAccessRequired));

            if (!CanViewPurchases || payload.Result is null)
            {
                ClearPurchases();
                ListErrorMessage = "Vous n'êtes pas autorisé à consulter les achats.";
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
            Trace.TraceError("Purchases initialization failed: {0}", exception);
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
                ClearSensitiveState();
                return;
            }

            var result = await GetPurchasesPageAsync(pageNumber, cancellation.Token);
            if (!CanApplyResult(identity, cancellation, version))
            {
                FailClosedIfSessionChanged(identity);
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
            Trace.TraceError("Purchases list loading failed: {0}", exception);
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

    private async Task SearchSuppliersCoreAsync(CancellationToken cancellationToken)
    {
        IsLookingUpSuppliers = true;
        LookupErrorMessage = null;
        SelectedLookupResult = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity) || !CanViewSuppliers)
            {
                return;
            }

            var query = new SupplierLookupQuery(
                string.IsNullOrWhiteSpace(SupplierSearchText) ? null : SupplierSearchText.Trim(),
                SupplierLookupLimit);
            var result = await _operationCoordinator.RunAsync(
                token => _supplierLookupService.SearchAsync(query, token),
                cancellation.Token);

            if (!IsSameAuthenticatedSession(identity) || cancellation.IsCancellationRequested || _isStopped)
            {
                SupplierResults.Clear();
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (!result.Success)
            {
                SupplierResults.Clear();
                if (result.FailureReason is SupplierLookupFailureReason.NotAuthenticated
                    or SupplierLookupFailureReason.Forbidden)
                {
                    CanViewSuppliers = false;
                    SelectedSupplier = null;
                    LookupErrorMessage = null;
                    FormErrorMessage = SupplierPermissionMessage;
                }
                else
                {
                    LookupErrorMessage = "Impossible de rechercher les fournisseurs.";
                }

                return;
            }

            SupplierResults.Clear();
            foreach (var item in result.Items)
            {
                SupplierResults.Add(new SupplierLookupItemViewModel(item));
            }

            if (SupplierResults.Count == 0)
            {
                LookupErrorMessage = "Aucun fournisseur trouvé.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the Shell closes.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Supplier lookup failed: {0}", exception);
            if (!_isStopped)
            {
                LookupErrorMessage = "Impossible de rechercher les fournisseurs.";
            }
        }
        finally
        {
            _operationCancellation = null;
            if (!_isStopped)
            {
                IsLookingUpSuppliers = false;
            }
        }
    }

    private async Task CreatePurchaseCoreAsync(CancellationToken cancellationToken)
    {
        FormErrorMessage = null;
        SuccessMessage = null;

        if (!TryCreatePurchaseCommand(out var command))
        {
            return;
        }

        IsSaving = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity)
                || !CanCreatePurchases
                || !CanViewSuppliers)
            {
                FormErrorMessage = CanViewSuppliers
                    ? "Vous n'êtes pas autorisé à créer un achat."
                    : SupplierPermissionMessage;
                return;
            }

            var result = await _operationCoordinator.RunAsync(
                token => _purchaseService.CreateAsync(command, token),
                cancellation.Token);
            if (!IsSameAuthenticatedSession(identity) || cancellation.IsCancellationRequested || _isStopped)
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (!result.Success)
            {
                if (result.FailureReason is CreatePurchaseFailureReason.NotAuthenticated
                    or CreatePurchaseFailureReason.NotAuthorized)
                {
                    CanCreatePurchases = false;
                }

                if (result.FailureReason == CreatePurchaseFailureReason.SupplierNotFound)
                {
                    SelectedSupplier = null;
                    SupplierResults.Clear();
                }

                FormErrorMessage = MapCreateFailure(result.FailureReason);
                return;
            }

            ResetCreateForm();
            IsCreateFormVisible = false;
            ResetFiltersWithoutLoading();
            SuccessMessage = "Achat enregistré.";

            var refreshResult = await GetPurchasesPageAsync(1, cancellation.Token);
            if (!IsSameAuthenticatedSession(identity) || cancellation.IsCancellationRequested || _isStopped)
            {
                FailClosedIfSessionChanged(identity);
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
            Trace.TraceError("Purchase creation failed: {0}", exception);
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

    private bool TryCreatePurchaseCommand(out CreatePurchaseCommand command)
    {
        command = null!;

        if (SelectedSupplier is not { } supplier)
        {
            FormErrorMessage = SupplierRequiredMessage;
            return false;
        }

        if (!MoneyParser.TryParsePositiveMinorUnits(TotalAmount, out var totalAmountMinor))
        {
            FormErrorMessage = InvalidAmountMessage;
            return false;
        }

        InitialSupplierPaymentInput? initialPayment = null;
        if (HasInitialPayment)
        {
            if (!MoneyParser.TryParsePositiveMinorUnits(InitialPaymentAmount, out var initialPaymentMinor))
            {
                FormErrorMessage = InvalidInitialPaymentMessage;
                return false;
            }

            if (SelectedPaymentMethod is not { } paymentMethod)
            {
                FormErrorMessage = "Sélectionnez un mode de paiement.";
                return false;
            }

            initialPayment = new InitialSupplierPaymentInput(
                initialPaymentMinor,
                paymentMethod.Value,
                NormalizeOptionalText(PaymentObservation));
        }

        command = new CreatePurchaseCommand(
            supplier.SupplierId,
            totalAmountMinor,
            NormalizeOptionalText(PurchaseObservation),
            DueDate is DateTime dueDate ? DateOnly.FromDateTime(dueDate.Date) : null,
            initialPayment);
        return true;
    }

    private Task<PurchaseListResult> GetPurchasesPageAsync(
        int pageNumber,
        CancellationToken cancellationToken) =>
        _operationCoordinator.RunAsync(
            token => _purchaseService.GetPageAsync(CreateListQuery(pageNumber), token),
            cancellationToken);

    private PurchaseListQuery CreateListQuery(int pageNumber) => new(
        pageNumber,
        PageSize,
        _appliedFromDate,
        _appliedToDate,
        _appliedSearchText);

    private void ApplyListResult(PurchaseListResult result)
    {
        Purchases.Clear();
        foreach (var item in result.Items)
        {
            Purchases.Add(new PurchaseListItemViewModel(item));
        }

        PageNumber = result.PageNumber;
        TotalPages = result.TotalPages;
        TotalCount = result.TotalCount;
        HasLoadedList = true;
        ListErrorMessage = null;
        NotifyPurchasesCollectionChanged();
    }

    private void HandleListFailure(
        PurchaseListFailureReason failureReason,
        bool preserveSuccess = false)
    {
        if (!preserveSuccess)
        {
            SuccessMessage = null;
        }

        if (failureReason is PurchaseListFailureReason.NotAuthenticated
            or PurchaseListFailureReason.Forbidden)
        {
            ClearPurchases();
            CanViewPurchases = false;
            CanCreatePurchases = false;
            ListErrorMessage = "Vous n'êtes pas autorisé à consulter les achats.";
            return;
        }

        ListErrorMessage = failureReason switch
        {
            PurchaseListFailureReason.InvalidDateRange => InvalidDateRangeMessage,
            PurchaseListFailureReason.InvalidPageNumber => "La page demandée n'est pas disponible.",
            PurchaseListFailureReason.InvalidPageSize => "La taille de page demandée n'est pas valide.",
            _ => ListLoadError
        };
    }

    private void ClearPurchases()
    {
        Purchases.Clear();
        PageNumber = 1;
        TotalPages = 0;
        TotalCount = 0;
        HasLoadedList = false;
        NotifyPurchasesCollectionChanged();
    }

    private void FailClosedIfSessionChanged(SessionIdentity identity)
    {
        if (!_isStopped && !IsSameAuthenticatedSession(identity))
        {
            ClearSensitiveState();
        }
    }

    private void ClearSensitiveState()
    {
        ClearPurchases();
        SupplierResults.Clear();
        SelectedLookupResult = null;
        SelectedSupplier = null;
        IsCreateFormVisible = false;
        CanViewPurchases = false;
        CanCreatePurchases = false;
        CanViewSuppliers = false;
        ListErrorMessage = "Votre session n'est plus active.";
        FormErrorMessage = null;
        LookupErrorMessage = null;
        SuccessMessage = null;
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
        PurchaseObservation = string.Empty;
        SelectedSupplier = null;
        SupplierSearchText = string.Empty;
        SelectedLookupResult = null;
        SupplierResults.Clear();
        HasInitialPayment = false;
        InitialPaymentAmount = string.Empty;
        SelectedPaymentMethod = PaymentMethods[0];
        PaymentObservation = string.Empty;
        FormErrorMessage = null;
        LookupErrorMessage = null;
    }

    private void SelectSupplier()
    {
        if (SelectedLookupResult is null || !CanUseSupplierLookup)
        {
            return;
        }

        SelectedSupplier = SelectedLookupResult;
        SelectedLookupResult = null;
        SupplierResults.Clear();
        LookupErrorMessage = null;
    }

    private void RemoveSelectedSupplier()
    {
        SelectedSupplier = null;
        SelectedLookupResult = null;
        SupplierResults.Clear();
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

    private static string MapCreateFailure(CreatePurchaseFailureReason failureReason) => failureReason switch
    {
        CreatePurchaseFailureReason.NotAuthenticated => "Votre session n'est plus active.",
        CreatePurchaseFailureReason.NotAuthorized => "Vous n'êtes pas autorisé à créer un achat.",
        CreatePurchaseFailureReason.InvalidTotalAmount => InvalidAmountMessage,
        CreatePurchaseFailureReason.SupplierNotFound =>
            "Le fournisseur sélectionné n'est plus disponible. Recherchez-le de nouveau.",
        CreatePurchaseFailureReason.InvalidInitialPaymentAmount => InvalidInitialPaymentMessage,
        CreatePurchaseFailureReason.InvalidPaymentMethod => "Le mode de paiement sélectionné n'est pas valide.",
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
            or SupplierRequiredMessage
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

    private void NotifyPurchasesCollectionChanged()
    {
        OnPropertyChanged(nameof(HasPurchases));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void NotifyFilterStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private void NotifyCreationPermissionChanged()
    {
        OnPropertyChanged(nameof(CanStartPurchaseCreation));
        OnPropertyChanged(nameof(ShowSupplierAccessRequired));
        RaiseCommandCanExecuteChanged();
    }

    private void RaiseCommandCanExecuteChanged()
    {
        _refreshRelayCommand?.RaiseCanExecuteChanged();
        _applyFiltersRelayCommand?.RaiseCanExecuteChanged();
        _resetFiltersRelayCommand?.RaiseCanExecuteChanged();
        _previousPageRelayCommand?.RaiseCanExecuteChanged();
        _nextPageRelayCommand?.RaiseCanExecuteChanged();
        _searchSuppliersRelayCommand?.RaiseCanExecuteChanged();
        _createPurchaseRelayCommand?.RaiseCanExecuteChanged();
        _showCreateFormRelayCommand?.RaiseCanExecuteChanged();
        _cancelCreateFormRelayCommand?.RaiseCanExecuteChanged();
        _selectSupplierRelayCommand?.RaiseCanExecuteChanged();
        _removeSupplierRelayCommand?.RaiseCanExecuteChanged();
    }

    private sealed record InitializationPayload(
        EffectiveModulePermission PurchasePermissions,
        EffectiveModulePermission SupplierPermissions,
        PurchaseListResult? Result);

    private readonly record struct SessionIdentity(
        long UserId,
        string? Username,
        UserRole Role);
}
