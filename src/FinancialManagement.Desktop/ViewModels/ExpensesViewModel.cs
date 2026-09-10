using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Employees;
using FinancialManagement.Application.ExpenseCategories;
using FinancialManagement.Application.Expenses;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class ExpensesViewModel : ViewModelBase
{
    private const int PageSize = 25;
    private const int EmployeeLookupLimit = 20;
    private const string ListLoadError = "Impossible de charger les dépenses.";
    private const string CreateError = "Impossible d'enregistrer la dépense.";
    private const string RefreshAfterCreateError =
        "La dépense a été enregistrée, mais l'actualisation de la liste a échoué.";
    private const string InvalidDateRangeMessage =
        "La date de début doit être antérieure ou égale à la date de fin.";
    private const string InvalidAmountMessage = "Le montant de la dépense est invalide.";
    private const string CategoryRequiredMessage =
        "Sélectionnez une catégorie avant d'enregistrer la dépense.";
    private const string PaymentMethodRequiredMessage = "Sélectionnez un moyen de paiement.";
    private const string SalaryEmployeeRequiredMessage =
        "Sélectionnez l'employé concerné pour une dépense de salaire.";

    private readonly IExpenseService _expenseService;
    private readonly IExpenseCategoryLookupService _categoryLookupService;
    private readonly IEmployeeLookupService _employeeLookupService;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly AsyncRelayCommand _refreshRelayCommand;
    private readonly AsyncRelayCommand _applyFiltersRelayCommand;
    private readonly AsyncRelayCommand _resetFiltersRelayCommand;
    private readonly AsyncRelayCommand _previousPageRelayCommand;
    private readonly AsyncRelayCommand _nextPageRelayCommand;
    private readonly AsyncRelayCommand _showCreateFormRelayCommand;
    private readonly AsyncRelayCommand _searchEmployeesRelayCommand;
    private readonly AsyncRelayCommand _createExpenseRelayCommand;
    private readonly RelayCommand _cancelCreateFormRelayCommand;
    private readonly RelayCommand _selectEmployeeRelayCommand;
    private readonly RelayCommand _removeEmployeeRelayCommand;
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
    private bool _isLoadingCategories;
    private bool _isSearchingEmployees;
    private bool _hasLoadedList;
    private bool _canViewExpenses;
    private bool _canCreateExpenses;
    private bool _isCreateFormVisible;
    private string? _listErrorMessage;
    private string? _formErrorMessage;
    private string? _categoryErrorMessage;
    private string? _employeeErrorMessage;
    private string? _successMessage;
    private ExpenseCategoryLookupItemViewModel? _selectedCategory;
    private string _amount = string.Empty;
    private PaymentMethodOptionViewModel? _selectedPaymentMethod;
    private string _employeeSearchText = string.Empty;
    private EmployeeLookupItemViewModel? _selectedEmployeeResult;
    private EmployeeLookupItemViewModel? _selectedEmployee;
    private string _observation = string.Empty;
    private bool _initialLoadRequested;
    private bool _isStopped;
    private long _loadVersion;
    private CancellationTokenSource? _operationCancellation;
    private Task _pendingOperation = Task.CompletedTask;

    public ExpensesViewModel(
        IExpenseService expenseService,
        IExpenseCategoryLookupService categoryLookupService,
        IEmployeeLookupService employeeLookupService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(expenseService);
        ArgumentNullException.ThrowIfNull(categoryLookupService);
        ArgumentNullException.ThrowIfNull(employeeLookupService);
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);

        _expenseService = expenseService;
        _categoryLookupService = categoryLookupService;
        _employeeLookupService = employeeLookupService;
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
        _showCreateFormRelayCommand = new AsyncRelayCommand(
            _ => ShowCreateFormAsync(),
            _ => CanCreateExpenses && !IsCreateFormVisible && !IsBusy && !_isStopped);
        _searchEmployeesRelayCommand = new AsyncRelayCommand(
            _ => SearchEmployeesAsync(),
            _ => CanSearchEmployees);
        _createExpenseRelayCommand = new AsyncRelayCommand(
            _ => CreateExpenseAsync(),
            _ => CanSubmitExpense);
        _cancelCreateFormRelayCommand = new RelayCommand(
            _ => CancelCreateForm(),
            _ => IsCreateFormVisible && !IsSaving && !_isStopped);
        _selectEmployeeRelayCommand = new RelayCommand(
            _ => SelectEmployee(),
            _ => SelectedEmployeeResult is not null && IsCreateFormVisible && !IsSaving);
        _removeEmployeeRelayCommand = new RelayCommand(
            _ => RemoveSelectedEmployee(),
            _ => SelectedEmployee is not null && IsCreateFormVisible && !IsSaving);

        RefreshCommand = _refreshRelayCommand;
        ApplyFiltersCommand = _applyFiltersRelayCommand;
        ResetFiltersCommand = _resetFiltersRelayCommand;
        PreviousPageCommand = _previousPageRelayCommand;
        NextPageCommand = _nextPageRelayCommand;
        ShowCreateFormCommand = _showCreateFormRelayCommand;
        SearchEmployeesCommand = _searchEmployeesRelayCommand;
        CreateExpenseCommand = _createExpenseRelayCommand;
        CancelCreateFormCommand = _cancelCreateFormRelayCommand;
        SelectEmployeeCommand = _selectEmployeeRelayCommand;
        RemoveEmployeeCommand = _removeEmployeeRelayCommand;
    }

    public ObservableCollection<ExpenseListItemViewModel> Expenses { get; } = [];
    public ObservableCollection<ExpenseCategoryLookupItemViewModel> Categories { get; } = [];
    public ObservableCollection<EmployeeLookupItemViewModel> EmployeeResults { get; } = [];
    public IReadOnlyList<PaymentMethodOptionViewModel> PaymentMethods { get; }

    public ICommand RefreshCommand { get; }
    public ICommand ApplyFiltersCommand { get; }
    public ICommand ResetFiltersCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand ShowCreateFormCommand { get; }
    public ICommand SearchEmployeesCommand { get; }
    public ICommand CreateExpenseCommand { get; }
    public ICommand CancelCreateFormCommand { get; }
    public ICommand SelectEmployeeCommand { get; }
    public ICommand RemoveEmployeeCommand { get; }

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
        0 => "Aucune dépense",
        1 => "1 dépense",
        _ => $"{TotalCount} dépenses"
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

    public bool IsLoadingCategories
    {
        get => _isLoadingCategories;
        private set
        {
            if (SetProperty(ref _isLoadingCategories, value))
            {
                NotifyBusyStateChanged();
            }
        }
    }

    public bool IsSearchingEmployees
    {
        get => _isSearchingEmployees;
        private set
        {
            if (SetProperty(ref _isSearchingEmployees, value))
            {
                OnPropertyChanged(nameof(CanSearchEmployees));
                OnPropertyChanged(nameof(CanSubmitExpense));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool IsBusy => IsLoading || IsSaving || IsLoadingCategories;
    public bool HasExpenses => Expenses.Count > 0;
    public bool ShowEmptyState => HasLoadedList && !HasExpenses && !IsLoading;
    public string EmptyStateMessage => HasActiveFilters
        ? "Aucune dépense ne correspond aux filtres."
        : "Aucune dépense enregistrée.";

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

    public bool CanViewExpenses
    {
        get => _canViewExpenses;
        private set => SetProperty(ref _canViewExpenses, value);
    }

    public bool CanCreateExpenses
    {
        get => _canCreateExpenses;
        private set
        {
            if (SetProperty(ref _canCreateExpenses, value))
            {
                OnPropertyChanged(nameof(CanSearchEmployees));
                OnPropertyChanged(nameof(CanSubmitExpense));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool IsCreateFormVisible
    {
        get => _isCreateFormVisible;
        private set
        {
            if (SetProperty(ref _isCreateFormVisible, value))
            {
                OnPropertyChanged(nameof(CanSearchEmployees));
                OnPropertyChanged(nameof(CanSubmitExpense));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool CanSearchEmployees =>
        CanCreateExpenses && IsCreateFormVisible && !IsSaving && !IsLoadingCategories
        && !IsSearchingEmployees && !_isStopped;
    public bool CanSubmitExpense =>
        CanCreateExpenses && IsCreateFormVisible && !IsBusy && !IsSearchingEmployees && !_isStopped;

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

    public bool HasCategoryError => !string.IsNullOrWhiteSpace(CategoryErrorMessage);
    public string? CategoryErrorMessage
    {
        get => _categoryErrorMessage;
        private set
        {
            if (SetProperty(ref _categoryErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasCategoryError));
            }
        }
    }

    public bool HasEmployeeError => !string.IsNullOrWhiteSpace(EmployeeErrorMessage);
    public string? EmployeeErrorMessage
    {
        get => _employeeErrorMessage;
        private set
        {
            if (SetProperty(ref _employeeErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasEmployeeError));
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

    public ExpenseCategoryLookupItemViewModel? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ClearFormValidationMessage();
                OnPropertyChanged(nameof(IsEmployeeRequired));
                OnPropertyChanged(nameof(EmployeeFieldLabel));
                OnPropertyChanged(nameof(CanSubmitExpense));
                _createExpenseRelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string Amount
    {
        get => _amount;
        set
        {
            if (SetProperty(ref _amount, value))
            {
                ClearFormValidationMessage();
            }
        }
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

    public string EmployeeSearchText
    {
        get => _employeeSearchText;
        set => SetProperty(ref _employeeSearchText, value);
    }

    public EmployeeLookupItemViewModel? SelectedEmployeeResult
    {
        get => _selectedEmployeeResult;
        set
        {
            if (SetProperty(ref _selectedEmployeeResult, value))
            {
                _selectEmployeeRelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public EmployeeLookupItemViewModel? SelectedEmployee
    {
        get => _selectedEmployee;
        private set
        {
            if (SetProperty(ref _selectedEmployee, value))
            {
                ClearFormValidationMessage();
                OnPropertyChanged(nameof(HasSelectedEmployee));
                OnPropertyChanged(nameof(EmployeeSelectionDescription));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedEmployee => SelectedEmployee is not null;
    public bool IsEmployeeRequired =>
        SelectedCategory?.ExpenseCategoryId == ExpenseCategoryDefaults.SalaryId;
    public string EmployeeFieldLabel => IsEmployeeRequired
        ? "Employé concerné *"
        : "Employé concerné (facultatif)";
    public string EmployeeSelectionDescription => SelectedEmployee?.DisplayLabel ?? "Aucun employé concerné";

    public string Observation
    {
        get => _observation;
        set => SetProperty(ref _observation, value);
    }

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

    public Task ShowCreateFormAsync(CancellationToken cancellationToken = default)
    {
        if (!CanCreateExpenses || IsCreateFormVisible || IsBusy || _isStopped)
        {
            return _pendingOperation;
        }

        _pendingOperation = ShowCreateFormCoreAsync(cancellationToken);
        return _pendingOperation;
    }

    public Task SearchEmployeesAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSearchEmployees)
        {
            return _pendingOperation;
        }

        _pendingOperation = SearchEmployeesCoreAsync(cancellationToken);
        return _pendingOperation;
    }

    public Task CreateExpenseAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSubmitExpense)
        {
            return _pendingOperation;
        }

        _pendingOperation = CreateExpenseCoreAsync(cancellationToken);
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
        Expenses.Clear();
        ResetCreateForm();
        IsCreateFormVisible = false;
        HasLoadedList = false;
        ListErrorMessage = null;
        SuccessMessage = null;
        IsLoading = false;
        IsSaving = false;
        IsLoadingCategories = false;
        IsSearchingEmployees = false;
        RaiseCommandCanExecuteChanged();
    }

    public Task StopAsync()
    {
        Stop();
        return _pendingOperation;
    }

    private bool CanLoadPage =>
        CanViewExpenses && !IsBusy && !IsCreateFormVisible && !_isStopped;

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
                ClearSensitiveState("Votre session n'est plus active.");
                return;
            }

            var query = CreateListQuery(1);
            var payload = await _operationCoordinator.RunAsync(
                async token =>
                {
                    var permissions = await _authorizationService.GetEffectivePermissionsAsync(
                        PermissionModule.Expenses,
                        token);
                    var result = permissions.CanView
                        ? await _expenseService.GetPageAsync(query, token)
                        : null;
                    return new InitializationPayload(permissions, result);
                },
                cancellation.Token);

            if (!CanApplyResult(identity, cancellation, version))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            CanViewExpenses = payload.Permissions.CanView;
            CanCreateExpenses = payload.Permissions.CanView && payload.Permissions.CanCreate;

            if (!CanViewExpenses || payload.Result is null)
            {
                ClearExpenses();
                ListErrorMessage = "Vous n'êtes pas autorisé à consulter les dépenses.";
                return;
            }

            ApplyListResult(payload.Result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the page or shell closes.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Expenses initialization failed: {0}", exception);
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
                ClearSensitiveState("Votre session n'est plus active.");
                return;
            }

            var result = await GetExpensesPageAsync(pageNumber, cancellation.Token);
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
            // Cancellation is expected when the page or shell closes.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Expenses list loading failed: {0}", exception);
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

    private async Task ShowCreateFormCoreAsync(CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearSensitiveState("Votre session n'est plus active.");
                return;
            }

            var isAuthorized = await _operationCoordinator.RunAsync(
                token => _authorizationService.CanAsync(
                    PermissionModule.Expenses,
                    PermissionAction.Create,
                    token),
                cancellation.Token);
            if (!IsSameAuthenticatedSession(identity) || cancellation.IsCancellationRequested || _isStopped)
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (!isAuthorized)
            {
                DisableCreation("Vous n'êtes pas autorisé à créer une dépense.");
                return;
            }

            ResetCreateForm();
            SuccessMessage = null;
            IsCreateFormVisible = true;
            IsLoadingCategories = true;

            var result = await _operationCoordinator.RunAsync(
                token => _categoryLookupService.GetActiveAsync(token),
                cancellation.Token);
            if (!IsSameAuthenticatedSession(identity)
                || cancellation.IsCancellationRequested
                || _isStopped
                || !IsCreateFormVisible)
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (!result.Success)
            {
                HandleCategoryFailure(result.FailureReason);
                return;
            }

            Categories.Clear();
            foreach (var item in result.Items)
            {
                Categories.Add(new ExpenseCategoryLookupItemViewModel(item));
            }

            if (Categories.Count == 0)
            {
                CategoryErrorMessage = "Aucune catégorie active n'est disponible.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the form or shell closes.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Expense category loading failed: {0}", exception);
            if (!_isStopped && IsCreateFormVisible)
            {
                CategoryErrorMessage = "Impossible de charger les catégories de dépenses.";
            }
        }
        finally
        {
            _operationCancellation = null;
            if (!_isStopped)
            {
                IsLoadingCategories = false;
            }
        }
    }

    private async Task SearchEmployeesCoreAsync(CancellationToken cancellationToken)
    {
        IsSearchingEmployees = true;
        EmployeeErrorMessage = null;
        SelectedEmployeeResult = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity) || !CanCreateExpenses)
            {
                return;
            }

            var query = new EmployeeLookupQuery(
                string.IsNullOrWhiteSpace(EmployeeSearchText) ? null : EmployeeSearchText.Trim(),
                EmployeeLookupLimit);
            var result = await _operationCoordinator.RunAsync(
                token => _employeeLookupService.SearchAsync(query, token),
                cancellation.Token);

            if (!IsSameAuthenticatedSession(identity)
                || cancellation.IsCancellationRequested
                || _isStopped
                || !IsCreateFormVisible)
            {
                EmployeeResults.Clear();
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (!result.Success)
            {
                EmployeeResults.Clear();
                if (result.FailureReason == EmployeeLookupFailureReason.NotAuthenticated)
                {
                    ClearSensitiveState("Votre session n'est plus active.");
                }
                else if (result.FailureReason == EmployeeLookupFailureReason.Forbidden)
                {
                    DisableCreation("Vous n'êtes plus autorisé à créer une dépense.");
                }
                else
                {
                    EmployeeErrorMessage =
                        "Impossible de rechercher les employés. Vous pouvez enregistrer sans employé.";
                }

                return;
            }

            EmployeeResults.Clear();
            foreach (var item in result.Items)
            {
                EmployeeResults.Add(new EmployeeLookupItemViewModel(item));
            }

            if (EmployeeResults.Count == 0)
            {
                EmployeeErrorMessage = "Aucun employé trouvé. Vous pouvez enregistrer sans employé.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the form or shell closes.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Employee lookup failed: {0}", exception);
            if (!_isStopped && IsCreateFormVisible)
            {
                EmployeeErrorMessage =
                    "Impossible de rechercher les employés. Vous pouvez enregistrer sans employé.";
            }
        }
        finally
        {
            _operationCancellation = null;
            if (!_isStopped)
            {
                IsSearchingEmployees = false;
            }
        }
    }

    private async Task CreateExpenseCoreAsync(CancellationToken cancellationToken)
    {
        FormErrorMessage = null;
        SuccessMessage = null;
        if (!TryCreateExpenseCommand(out var command))
        {
            return;
        }

        IsSaving = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity) || !CanCreateExpenses)
            {
                FormErrorMessage = "Vous n'êtes pas autorisé à créer une dépense.";
                return;
            }

            var result = await _operationCoordinator.RunAsync(
                token => _expenseService.CreateAsync(command, token),
                cancellation.Token);
            if (!IsSameAuthenticatedSession(identity) || cancellation.IsCancellationRequested || _isStopped)
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (!result.Success)
            {
                HandleCreateFailure(result.FailureReason);
                return;
            }

            ResetCreateForm();
            IsCreateFormVisible = false;
            ResetFiltersWithoutLoading();
            SuccessMessage = "Dépense enregistrée.";

            var refreshResult = await GetExpensesPageAsync(1, cancellation.Token);
            if (!IsSameAuthenticatedSession(identity) || cancellation.IsCancellationRequested || _isStopped)
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (refreshResult.Success)
            {
                ApplyListResult(refreshResult);
            }
            else if (refreshResult.FailureReason is ExpenseListFailureReason.NotAuthenticated
                     or ExpenseListFailureReason.Forbidden)
            {
                HandleListFailure(refreshResult.FailureReason, preserveSuccess: true);
            }
            else
            {
                HandleListFailure(refreshResult.FailureReason, preserveSuccess: true);
                ListErrorMessage = RefreshAfterCreateError;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the form or shell closes.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Expense creation failed: {0}", exception);
            if (!_isStopped && IsCreateFormVisible)
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

    private bool TryCreateExpenseCommand(out CreateExpenseCommand command)
    {
        command = null!;

        if (SelectedCategory is not { } category)
        {
            FormErrorMessage = CategoryRequiredMessage;
            return false;
        }

        if (category.ExpenseCategoryId == ExpenseCategoryDefaults.SalaryId
            && SelectedEmployee is null)
        {
            FormErrorMessage = SalaryEmployeeRequiredMessage;
            return false;
        }

        if (!MoneyParser.TryParsePositiveMinorUnits(Amount, out var amountMinor))
        {
            FormErrorMessage = InvalidAmountMessage;
            return false;
        }

        if (SelectedPaymentMethod is not { } paymentMethod)
        {
            FormErrorMessage = PaymentMethodRequiredMessage;
            return false;
        }

        command = new CreateExpenseCommand(
            category.ExpenseCategoryId,
            amountMinor,
            paymentMethod.Value,
            SelectedEmployee?.UserId,
            NormalizeOptionalText(Observation));
        return true;
    }

    private Task<ExpenseListResult> GetExpensesPageAsync(
        int pageNumber,
        CancellationToken cancellationToken) =>
        _operationCoordinator.RunAsync(
            token => _expenseService.GetPageAsync(CreateListQuery(pageNumber), token),
            cancellationToken);

    private ExpenseListQuery CreateListQuery(int pageNumber) => new(
        pageNumber,
        PageSize,
        _appliedFromDate,
        _appliedToDate,
        _appliedSearchText);

    private void ApplyListResult(ExpenseListResult result)
    {
        Expenses.Clear();
        foreach (var item in result.Items)
        {
            Expenses.Add(new ExpenseListItemViewModel(item));
        }

        PageNumber = result.PageNumber;
        TotalPages = result.TotalPages;
        TotalCount = result.TotalCount;
        HasLoadedList = true;
        ListErrorMessage = null;
        NotifyExpensesCollectionChanged();
    }

    private void HandleListFailure(
        ExpenseListFailureReason failureReason,
        bool preserveSuccess = false)
    {
        if (!preserveSuccess)
        {
            SuccessMessage = null;
        }

        if (failureReason == ExpenseListFailureReason.NotAuthenticated)
        {
            ClearSensitiveState("Votre session n'est plus active.", preserveSuccess);
            return;
        }

        if (failureReason == ExpenseListFailureReason.Forbidden)
        {
            ClearSensitiveState("Vous n'êtes pas autorisé à consulter les dépenses.", preserveSuccess);
            return;
        }

        ListErrorMessage = failureReason switch
        {
            ExpenseListFailureReason.InvalidDateRange => InvalidDateRangeMessage,
            ExpenseListFailureReason.InvalidPageNumber => "La page demandée n'est pas disponible.",
            ExpenseListFailureReason.InvalidPageSize => "La taille de page demandée n'est pas valide.",
            _ => ListLoadError
        };
    }

    private void HandleCreateFailure(CreateExpenseFailureReason failureReason)
    {
        if (failureReason == CreateExpenseFailureReason.NotAuthenticated)
        {
            ClearSensitiveState("Votre session n'est plus active.");
            return;
        }

        if (failureReason == CreateExpenseFailureReason.NotAuthorized)
        {
            DisableCreation("Vous n'êtes plus autorisé à créer une dépense.");
            return;
        }

        if (failureReason is CreateExpenseFailureReason.ExpenseCategoryNotFound
            or CreateExpenseFailureReason.ExpenseCategoryInactive)
        {
            SelectedCategory = null;
            Categories.Clear();
        }

        if (failureReason is CreateExpenseFailureReason.ConcernedEmployeeNotFound
            or CreateExpenseFailureReason.ConcernedEmployeeNotEligible)
        {
            SelectedEmployee = null;
            SelectedEmployeeResult = null;
            EmployeeResults.Clear();
        }

        FormErrorMessage = failureReason switch
        {
            CreateExpenseFailureReason.InvalidAmount => InvalidAmountMessage,
            CreateExpenseFailureReason.InvalidPaymentMethod => "Le moyen de paiement sélectionné n'est pas valide.",
            CreateExpenseFailureReason.ExpenseCategoryNotFound => "La catégorie sélectionnée n'existe plus.",
            CreateExpenseFailureReason.ExpenseCategoryInactive => "La catégorie sélectionnée n'est plus disponible.",
            CreateExpenseFailureReason.ConcernedEmployeeRequired => SalaryEmployeeRequiredMessage,
            CreateExpenseFailureReason.ConcernedEmployeeNotFound => "L'employé sélectionné n'existe plus.",
            CreateExpenseFailureReason.ConcernedEmployeeNotEligible =>
                "L'utilisateur sélectionné ne peut pas être associé à cette dépense.",
            _ => CreateError
        };
    }

    private void HandleCategoryFailure(ExpenseCategoryLookupFailureReason failureReason)
    {
        Categories.Clear();
        SelectedCategory = null;
        if (failureReason == ExpenseCategoryLookupFailureReason.NotAuthenticated)
        {
            ClearSensitiveState("Votre session n'est plus active.");
        }
        else if (failureReason == ExpenseCategoryLookupFailureReason.Forbidden)
        {
            DisableCreation("Vous n'êtes plus autorisé à créer une dépense.");
        }
        else
        {
            CategoryErrorMessage = "Impossible de charger les catégories de dépenses.";
        }
    }

    private void ClearExpenses()
    {
        Expenses.Clear();
        PageNumber = 1;
        TotalPages = 0;
        TotalCount = 0;
        HasLoadedList = false;
        NotifyExpensesCollectionChanged();
    }

    private void ClearSensitiveState(string message, bool preserveSuccess = false)
    {
        ClearExpenses();
        ResetCreateForm();
        IsCreateFormVisible = false;
        CanViewExpenses = false;
        CanCreateExpenses = false;
        ListErrorMessage = message;
        if (!preserveSuccess)
        {
            SuccessMessage = null;
        }
    }

    private void DisableCreation(string message)
    {
        ResetCreateForm();
        IsCreateFormVisible = false;
        CanCreateExpenses = false;
        ListErrorMessage = message;
    }

    private void FailClosedIfSessionChanged(SessionIdentity identity)
    {
        if (!_isStopped && !IsSameAuthenticatedSession(identity))
        {
            ClearSensitiveState("Votre session n'est plus active.");
        }
    }

    private void CancelCreateForm()
    {
        _operationCancellation?.Cancel();
        ResetCreateForm();
        IsCreateFormVisible = false;
    }

    private void ResetCreateForm()
    {
        Categories.Clear();
        SelectedCategory = null;
        Amount = string.Empty;
        SelectedPaymentMethod = PaymentMethods[0];
        EmployeeSearchText = string.Empty;
        SelectedEmployeeResult = null;
        SelectedEmployee = null;
        EmployeeResults.Clear();
        Observation = string.Empty;
        FormErrorMessage = null;
        CategoryErrorMessage = null;
        EmployeeErrorMessage = null;
    }

    private void SelectEmployee()
    {
        if (SelectedEmployeeResult is null || !IsCreateFormVisible || IsSaving)
        {
            return;
        }

        SelectedEmployee = SelectedEmployeeResult;
        SelectedEmployeeResult = null;
        EmployeeResults.Clear();
        EmployeeErrorMessage = null;
    }

    private void RemoveSelectedEmployee()
    {
        SelectedEmployee = null;
        SelectedEmployeeResult = null;
        EmployeeResults.Clear();
        EmployeeErrorMessage = null;
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
            or CategoryRequiredMessage
            or PaymentMethodRequiredMessage)
        {
            FormErrorMessage = null;
        }
    }

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(CanSearchEmployees));
        OnPropertyChanged(nameof(CanSubmitExpense));
        RaiseCommandCanExecuteChanged();
    }

    private void NotifyPaginationChanged()
    {
        OnPropertyChanged(nameof(PageDescription));
        RaiseCommandCanExecuteChanged();
    }

    private void NotifyExpensesCollectionChanged()
    {
        OnPropertyChanged(nameof(HasExpenses));
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
        _showCreateFormRelayCommand?.RaiseCanExecuteChanged();
        _searchEmployeesRelayCommand?.RaiseCanExecuteChanged();
        _createExpenseRelayCommand?.RaiseCanExecuteChanged();
        _cancelCreateFormRelayCommand?.RaiseCanExecuteChanged();
        _selectEmployeeRelayCommand?.RaiseCanExecuteChanged();
        _removeEmployeeRelayCommand?.RaiseCanExecuteChanged();
    }

    private sealed record InitializationPayload(
        EffectiveModulePermission Permissions,
        ExpenseListResult? Result);

    private readonly record struct SessionIdentity(
        long UserId,
        string? Username,
        UserRole Role);
}
