using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public abstract class PartyManagementViewModelBase : ViewModelBase
{
    private const int PageSize = 25;

    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly PermissionModule _permissionModule;
    private readonly string _partyKindLower;
    private readonly string _partyKindCapitalized;
    private readonly string _partyPluralLower;
    private readonly AsyncRelayCommand _refreshRelayCommand;
    private readonly AsyncRelayCommand _applySearchRelayCommand;
    private readonly AsyncRelayCommand _resetSearchRelayCommand;
    private readonly AsyncRelayCommand _previousPageRelayCommand;
    private readonly AsyncRelayCommand _nextPageRelayCommand;
    private readonly RelayCommand _showCreateFormRelayCommand;
    private readonly AsyncRelayCommand _editRelayCommand;
    private readonly RelayCommand _cancelFormRelayCommand;
    private readonly AsyncRelayCommand _saveRelayCommand;
    private readonly RelayCommand _requestDeleteRelayCommand;
    private readonly RelayCommand _cancelDeleteRelayCommand;
    private readonly AsyncRelayCommand _confirmDeleteRelayCommand;
    private string _searchText = string.Empty;
    private string? _appliedSearchText;
    private int _pageNumber = 1;
    private int _totalPages;
    private int _totalCount;
    private bool _hasLoadedList;
    private bool _isLoading;
    private bool _isLoadingDetail;
    private bool _isSaving;
    private bool _isDeleting;
    private bool _canView;
    private bool _canCreate;
    private bool _canModify;
    private bool _canDelete;
    private bool _isFormVisible;
    private bool _isEditMode;
    private long? _editingId;
    private string _name = string.Empty;
    private string _phone = string.Empty;
    private string _address = string.Empty;
    private string _company = string.Empty;
    private string _identifier = string.Empty;
    private string _observation = string.Empty;
    private bool _isDeleteConfirmationVisible;
    private PartyListItemViewModel? _partyPendingDelete;
    private string? _listErrorMessage;
    private string? _formErrorMessage;
    private string? _deleteErrorMessage;
    private string? _successMessage;
    private bool _initialLoadRequested;
    private bool _isStopped;
    private long _requestVersion;
    private CancellationTokenSource? _operationCancellation;
    private Task _pendingOperation = Task.CompletedTask;

    protected PartyManagementViewModelBase(
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator,
        PermissionModule permissionModule,
        string partyKindLower,
        string partyKindCapitalized,
        string partyPluralLower,
        string pageTitle,
        string pageDescription,
        string balanceColumnHeader)
    {
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyKindLower);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyKindCapitalized);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyPluralLower);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(balanceColumnHeader);

        _currentUserSession = currentUserSession;
        _authorizationService = authorizationService;
        _operationCoordinator = operationCoordinator;
        _permissionModule = permissionModule;
        _partyKindLower = partyKindLower;
        _partyKindCapitalized = partyKindCapitalized;
        _partyPluralLower = partyPluralLower;
        PageTitle = pageTitle;
        PageDescription = pageDescription;
        BalanceColumnHeader = balanceColumnHeader;

        _refreshRelayCommand = new AsyncRelayCommand(
            _ => RefreshAsync(),
            _ => CanLoadPage);
        _applySearchRelayCommand = new AsyncRelayCommand(
            _ => ApplySearchAsync(),
            _ => CanLoadPage);
        _resetSearchRelayCommand = new AsyncRelayCommand(
            _ => ResetSearchAsync(),
            _ => CanLoadPage);
        _previousPageRelayCommand = new AsyncRelayCommand(
            _ => LoadPageAsync(PageNumber - 1),
            _ => CanLoadPage && PageNumber > 1);
        _nextPageRelayCommand = new AsyncRelayCommand(
            _ => LoadPageAsync(PageNumber + 1),
            _ => CanLoadPage && TotalPages > 0 && PageNumber < TotalPages);
        _showCreateFormRelayCommand = new RelayCommand(
            _ => ShowCreateForm(),
            _ => CanCreate && CanOpenOverlay);
        _editRelayCommand = new AsyncRelayCommand(
            parameter => parameter is PartyListItemViewModel item
                ? BeginEditAsync(item)
                : Task.CompletedTask,
            parameter => parameter is PartyListItemViewModel && CanModify && CanOpenOverlay);
        _cancelFormRelayCommand = new RelayCommand(
            _ => CancelForm(),
            _ => IsFormVisible && !IsBusy && !_isStopped);
        _saveRelayCommand = new AsyncRelayCommand(
            _ => SaveAsync(),
            _ => IsFormVisible && !IsBusy && !_isStopped && (IsEditMode ? CanModify : CanCreate));
        _requestDeleteRelayCommand = new RelayCommand(
            parameter => RequestDelete(parameter as PartyListItemViewModel),
            parameter => parameter is PartyListItemViewModel && CanDelete && CanOpenOverlay);
        _cancelDeleteRelayCommand = new RelayCommand(
            _ => CancelDelete(),
            _ => IsDeleteConfirmationVisible && !IsBusy && !_isStopped);
        _confirmDeleteRelayCommand = new AsyncRelayCommand(
            _ => ConfirmDeleteAsync(),
            _ => IsDeleteConfirmationVisible && PartyPendingDelete is not null && CanDelete && !IsBusy && !_isStopped);

        RefreshCommand = _refreshRelayCommand;
        ApplySearchCommand = _applySearchRelayCommand;
        ResetSearchCommand = _resetSearchRelayCommand;
        PreviousPageCommand = _previousPageRelayCommand;
        NextPageCommand = _nextPageRelayCommand;
        ShowCreateFormCommand = _showCreateFormRelayCommand;
        EditCommand = _editRelayCommand;
        CancelFormCommand = _cancelFormRelayCommand;
        SaveCommand = _saveRelayCommand;
        RequestDeleteCommand = _requestDeleteRelayCommand;
        CancelDeleteCommand = _cancelDeleteRelayCommand;
        ConfirmDeleteCommand = _confirmDeleteRelayCommand;
    }

    public ObservableCollection<PartyListItemViewModel> Items { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand ApplySearchCommand { get; }

    public ICommand ResetSearchCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand ShowCreateFormCommand { get; }

    public ICommand EditCommand { get; }

    public ICommand CancelFormCommand { get; }

    public ICommand SaveCommand { get; }

    public ICommand RequestDeleteCommand { get; }

    public ICommand CancelDeleteCommand { get; }

    public ICommand ConfirmDeleteCommand { get; }

    public string PageTitle { get; }

    public string PageDescription { get; }

    public string BalanceColumnHeader { get; }

    public string NewItemLabel => $"Nouveau {_partyKindLower}";

    public string NewItemAutomationName => $"Créer un nouveau {_partyKindLower}";

    public string SearchAutomationName => $"Rechercher {_partyKindLower} par nom ou identifiant";

    public string ListAutomationName => $"Liste des {_partyPluralLower}";

    public string LoadingMessage => $"Chargement des {_partyPluralLower}…";

    public string SavingMessage => IsEditMode
        ? $"Modification du {_partyKindLower}…"
        : $"Enregistrement du {_partyKindLower}…";

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public int PageNumber
    {
        get => _pageNumber;
        private set
        {
            if (SetProperty(ref _pageNumber, value))
            {
                OnPropertyChanged(nameof(PageDescriptionText));
                RaiseCommandCanExecuteChanged();
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
                OnPropertyChanged(nameof(PageDescriptionText));
                RaiseCommandCanExecuteChanged();
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

    public string PageDescriptionText => TotalPages == 0
        ? "Page 0 / 0"
        : $"Page {PageNumber} / {TotalPages}";

    public string TotalCountDescription => TotalCount switch
    {
        0 => $"Aucun {_partyKindLower}",
        1 => $"1 {_partyKindLower}",
        _ => $"{TotalCount} {_partyPluralLower}"
    };

    public bool HasItems => Items.Count > 0;

    public bool ShowEmptyState => HasLoadedList && !IsLoading && Items.Count == 0;

    public string EmptyStateMessage => string.IsNullOrWhiteSpace(_appliedSearchText)
        ? $"Aucun {_partyKindLower} enregistré."
        : $"Aucun {_partyKindLower} ne correspond à la recherche.";

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

    public bool IsLoadingDetail
    {
        get => _isLoadingDetail;
        private set
        {
            if (SetProperty(ref _isLoadingDetail, value))
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
                OnPropertyChanged(nameof(SavingMessage));
                NotifyBusyStateChanged();
            }
        }
    }

    public bool IsDeleting
    {
        get => _isDeleting;
        private set
        {
            if (SetProperty(ref _isDeleting, value))
            {
                NotifyBusyStateChanged();
            }
        }
    }

    public bool IsBusy => IsLoading || IsLoadingDetail || IsSaving || IsDeleting;

    public bool CanView
    {
        get => _canView;
        private set
        {
            if (SetProperty(ref _canView, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool CanCreate
    {
        get => _canCreate;
        private set
        {
            if (SetProperty(ref _canCreate, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool CanModify
    {
        get => _canModify;
        private set
        {
            if (SetProperty(ref _canModify, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool CanDelete
    {
        get => _canDelete;
        private set
        {
            if (SetProperty(ref _canDelete, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool IsFormVisible
    {
        get => _isFormVisible;
        private set
        {
            if (SetProperty(ref _isFormVisible, value))
            {
                OnPropertyChanged(nameof(IsOverlayVisible));
                OnPropertyChanged(nameof(FormTitle));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool IsEditMode
    {
        get => _isEditMode;
        private set
        {
            if (SetProperty(ref _isEditMode, value))
            {
                OnPropertyChanged(nameof(FormTitle));
                OnPropertyChanged(nameof(SaveButtonLabel));
                OnPropertyChanged(nameof(SavingMessage));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string FormTitle => IsEditMode
        ? $"Modifier le {_partyKindLower}"
        : $"Nouveau {_partyKindLower}";

    public string SaveButtonLabel => IsEditMode ? "Enregistrer les modifications" : "Enregistrer";

    public string Name
    {
        get => _name;
        set
        {
            if (SetProperty(ref _name, value))
            {
                ClearFormError();
            }
        }
    }

    public string Phone
    {
        get => _phone;
        set => SetProperty(ref _phone, value);
    }

    public string Address
    {
        get => _address;
        set => SetProperty(ref _address, value);
    }

    public string Company
    {
        get => _company;
        set => SetProperty(ref _company, value);
    }

    public string Identifier
    {
        get => _identifier;
        set => SetProperty(ref _identifier, value);
    }

    public string Observation
    {
        get => _observation;
        set => SetProperty(ref _observation, value);
    }

    public bool IsDeleteConfirmationVisible
    {
        get => _isDeleteConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isDeleteConfirmationVisible, value))
            {
                OnPropertyChanged(nameof(IsOverlayVisible));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool IsOverlayVisible => IsFormVisible || IsDeleteConfirmationVisible;

    public PartyListItemViewModel? PartyPendingDelete
    {
        get => _partyPendingDelete;
        private set
        {
            if (SetProperty(ref _partyPendingDelete, value))
            {
                OnPropertyChanged(nameof(DeleteConfirmationQuestion));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string DeleteConfirmationQuestion =>
        $"Supprimer définitivement ce {_partyKindLower} ?";

    public string DeleteConfirmationDescription => PartyPendingDelete is null
        ? string.Empty
        : $"{PartyPendingDelete.Name} sera supprimé définitivement. Cette action est irréversible.";

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

    public bool HasListError => !string.IsNullOrWhiteSpace(ListErrorMessage);

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

    public bool HasFormError => !string.IsNullOrWhiteSpace(FormErrorMessage);

    public string? DeleteErrorMessage
    {
        get => _deleteErrorMessage;
        private set
        {
            if (SetProperty(ref _deleteErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasDeleteError));
            }
        }
    }

    public bool HasDeleteError => !string.IsNullOrWhiteSpace(DeleteErrorMessage);

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

    public bool HasSuccessMessage => !string.IsNullOrWhiteSpace(SuccessMessage);

    protected abstract Task<PartyPageResult> GetPageFromBackendAsync(
        int pageNumber,
        int pageSize,
        string? searchText,
        CancellationToken cancellationToken);

    protected abstract Task<PartyDetailResult> GetDetailFromBackendAsync(
        long id,
        CancellationToken cancellationToken);

    protected abstract Task<PartyMutationResult> CreateOnBackendAsync(
        PartyFormData form,
        CancellationToken cancellationToken);

    protected abstract Task<PartyMutationResult> UpdateOnBackendAsync(
        long id,
        PartyFormData form,
        CancellationToken cancellationToken);

    protected abstract Task<PartyMutationResult> DeleteOnBackendAsync(
        long id,
        CancellationToken cancellationToken);

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_isStopped)
        {
            return _pendingOperation;
        }

        if (!_initialLoadRequested)
        {
            _initialLoadRequested = true;
            _pendingOperation = InitializeCoreAsync(cancellationToken);
        }
        else if (!IsBusy)
        {
            _pendingOperation = LoadPageCoreAsync(PageNumber, cancellationToken);
        }

        return _pendingOperation;
    }

    public void Stop()
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;
        Interlocked.Increment(ref _requestVersion);
        _operationCancellation?.Cancel();
        ClearProtectedState(null);
        IsLoading = false;
        IsLoadingDetail = false;
        IsSaving = false;
        IsDeleting = false;
        RaiseCommandCanExecuteChanged();
    }

    public Task StopAsync()
    {
        Stop();
        return _pendingOperation;
    }

    private bool CanLoadPage => CanView && CanOpenOverlay;

    private bool CanOpenOverlay => !IsBusy && !IsOverlayVisible && !_isStopped;

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ListErrorMessage = null;
        var version = Interlocked.Increment(ref _requestVersion);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var payload = await _operationCoordinator.RunAsync(
                async token =>
                {
                    var permissions = await _authorizationService.GetEffectivePermissionsAsync(
                        _permissionModule,
                        token);
                    var permanentDelete =
                        _authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.PermanentDelete);
                    var page = permissions.CanView
                        ? await GetPageFromBackendAsync(1, PageSize, null, token)
                        : null;
                    return new PartyInitializationPayload(permissions, permanentDelete, page);
                },
                cancellation.Token);

            if (!CanApplyResult(identity, cancellation, version))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            ApplyPermissions(payload.Permissions, payload.PermanentDelete);
            if (!CanView || payload.Page is null)
            {
                ClearItems();
                ListErrorMessage = $"Vous n'êtes pas autorisé à consulter les {_partyPluralLower}.";
                return;
            }

            ApplyPage(payload.Page);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Expected when the shell closes or the authenticated session ends.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} initialization failed: {1}", PageTitle, exception);
            if (!_isStopped)
            {
                ListErrorMessage = GetListLoadError();
            }
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
            }

            if (!_isStopped)
            {
                IsLoading = false;
            }
        }
    }

    private Task RefreshAsync() => LoadPageAsync(PageNumber);

    private Task ApplySearchAsync()
    {
        _appliedSearchText = NormalizeOptional(SearchText);
        OnPropertyChanged(nameof(EmptyStateMessage));
        return LoadPageAsync(1);
    }

    private Task ResetSearchAsync()
    {
        SearchText = string.Empty;
        _appliedSearchText = null;
        OnPropertyChanged(nameof(EmptyStateMessage));
        return LoadPageAsync(1);
    }

    private Task LoadPageAsync(int pageNumber)
    {
        if (!CanLoadPage)
        {
            return _pendingOperation;
        }

        _pendingOperation = LoadPageCoreAsync(pageNumber, CancellationToken.None);
        return _pendingOperation;
    }

    private async Task LoadPageCoreAsync(int pageNumber, CancellationToken cancellationToken)
    {
        IsLoading = true;
        ListErrorMessage = null;
        SuccessMessage = null;
        var version = Interlocked.Increment(ref _requestVersion);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var result = await _operationCoordinator.RunAsync(
                token => GetPageFromBackendAsync(pageNumber, PageSize, _appliedSearchText, token),
                cancellation.Token);
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

            ApplyPage(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Expected during lifecycle cancellation.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} list loading failed: {1}", PageTitle, exception);
            if (!_isStopped)
            {
                ListErrorMessage = GetListLoadError();
            }
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
            }

            if (!_isStopped)
            {
                IsLoading = false;
            }
        }
    }

    private void ShowCreateForm()
    {
        ResetForm();
        IsEditMode = false;
        _editingId = null;
        SuccessMessage = null;
        IsFormVisible = true;
    }

    private Task BeginEditAsync(PartyListItemViewModel item)
    {
        if (!CanModify || !CanOpenOverlay)
        {
            return _pendingOperation;
        }

        _pendingOperation = BeginEditCoreAsync(item.Id, CancellationToken.None);
        return _pendingOperation;
    }

    private async Task BeginEditCoreAsync(long id, CancellationToken cancellationToken)
    {
        IsLoadingDetail = true;
        ListErrorMessage = null;
        SuccessMessage = null;
        var version = Interlocked.Increment(ref _requestVersion);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var payload = await _operationCoordinator.RunAsync(
                async token =>
                {
                    var permissions = await _authorizationService.GetEffectivePermissionsAsync(
                        _permissionModule,
                        token);
                    var permanentDelete =
                        _authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.PermanentDelete);
                    var detail = permissions.CanView && permissions.CanModify
                        ? await GetDetailFromBackendAsync(id, token)
                        : PartyDetailResult.Failed(PartyFailureReason.Forbidden);
                    return new PartyDetailPayload(permissions, permanentDelete, detail);
                },
                cancellation.Token);

            if (!CanApplyResult(identity, cancellation, version))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            ApplyPermissions(payload.Permissions, payload.PermanentDelete);
            if (!CanView)
            {
                ClearProtectedState($"Vous n'êtes plus autorisé à consulter les {_partyPluralLower}.");
                return;
            }

            if (!CanModify || !payload.Detail.Success || payload.Detail.Item is null)
            {
                HandleDetailFailure(payload.Detail.FailureReason);
                return;
            }

            PopulateForm(payload.Detail.Item);
            _editingId = payload.Detail.Item.Id;
            IsEditMode = true;
            IsFormVisible = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Expected during lifecycle cancellation.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} detail loading failed: {1}", PageTitle, exception);
            if (!_isStopped)
            {
                ListErrorMessage = $"Impossible de charger le détail du {_partyKindLower}.";
            }
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
            }

            if (!_isStopped)
            {
                IsLoadingDetail = false;
            }
        }
    }

    private void CancelForm()
    {
        ResetForm();
        IsFormVisible = false;
    }

    private Task SaveAsync()
    {
        if (!TryCreateFormData(out var form))
        {
            return Task.CompletedTask;
        }

        _pendingOperation = SaveCoreAsync(form, CancellationToken.None);
        return _pendingOperation;
    }

    private async Task SaveCoreAsync(PartyFormData form, CancellationToken cancellationToken)
    {
        IsSaving = true;
        FormErrorMessage = null;
        SuccessMessage = null;
        var version = Interlocked.Increment(ref _requestVersion);
        var editingId = _editingId;
        var editMode = IsEditMode;
        var targetPage = editMode ? PageNumber : 1;
        var targetSearch = editMode ? _appliedSearchText : null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var payload = await _operationCoordinator.RunAsync(
                async token =>
                {
                    var permissions = await _authorizationService.GetEffectivePermissionsAsync(
                        _permissionModule,
                        token);
                    var permanentDelete =
                        _authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.PermanentDelete);
                    var authorized = permissions.CanView
                        && (editMode ? permissions.CanModify : permissions.CanCreate);
                    var mutation = !authorized
                        ? PartyMutationResult.Failed(PartyFailureReason.NotAuthorized)
                        : editMode
                            ? editingId is long id
                                ? await UpdateOnBackendAsync(id, form, token)
                                : PartyMutationResult.Failed(PartyFailureReason.InvalidId)
                            : await CreateOnBackendAsync(form, token);
                    var page = mutation.Success && permissions.CanView
                        ? await GetPageFromBackendAsync(targetPage, PageSize, targetSearch, token)
                        : null;
                    return new PartySavePayload(permissions, permanentDelete, mutation, page);
                },
                cancellation.Token);

            if (!CanApplyResult(identity, cancellation, version))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            ApplyPermissions(payload.Permissions, payload.PermanentDelete);
            if (!CanView)
            {
                ClearProtectedState($"Vous n'êtes plus autorisé à consulter les {_partyPluralLower}.");
                return;
            }

            if (!payload.Mutation.Success)
            {
                HandleMutationFailure(payload.Mutation.FailureReason, editMode);
                return;
            }

            ResetForm();
            IsFormVisible = false;
            if (!editMode)
            {
                SearchText = string.Empty;
                _appliedSearchText = null;
                OnPropertyChanged(nameof(EmptyStateMessage));
            }

            SuccessMessage = editMode
                ? $"{_partyKindCapitalized} modifié."
                : $"{_partyKindCapitalized} enregistré.";

            if (payload.Page is { Success: true })
            {
                ApplyPage(payload.Page);
            }
            else if (payload.Page is not null)
            {
                HandleListFailure(payload.Page.FailureReason, preserveSuccess: true);
            }
            else
            {
                ListErrorMessage =
                    $"Le {_partyKindLower} a été enregistré, mais l'actualisation de la liste a échoué.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Expected during lifecycle cancellation.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} save failed: {1}", PageTitle, exception);
            if (!_isStopped && IsFormVisible)
            {
                FormErrorMessage = $"Impossible d'enregistrer le {_partyKindLower}.";
            }
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
            }

            if (!_isStopped)
            {
                IsSaving = false;
            }
        }
    }

    private void RequestDelete(PartyListItemViewModel? item)
    {
        if (item is null || !CanDelete || !CanOpenOverlay)
        {
            return;
        }

        PartyPendingDelete = item;
        DeleteErrorMessage = null;
        SuccessMessage = null;
        IsDeleteConfirmationVisible = true;
        OnPropertyChanged(nameof(DeleteConfirmationDescription));
    }

    private void CancelDelete()
    {
        IsDeleteConfirmationVisible = false;
        PartyPendingDelete = null;
        DeleteErrorMessage = null;
    }

    private Task ConfirmDeleteAsync()
    {
        if (PartyPendingDelete is not { } item)
        {
            return Task.CompletedTask;
        }

        _pendingOperation = DeleteCoreAsync(item, CancellationToken.None);
        return _pendingOperation;
    }

    private async Task DeleteCoreAsync(
        PartyListItemViewModel item,
        CancellationToken cancellationToken)
    {
        IsDeleting = true;
        DeleteErrorMessage = null;
        SuccessMessage = null;
        var version = Interlocked.Increment(ref _requestVersion);
        var targetPage = PageNumber > 1 && Items.Count == 1 ? PageNumber - 1 : PageNumber;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var payload = await _operationCoordinator.RunAsync(
                async token =>
                {
                    var permissions = await _authorizationService.GetEffectivePermissionsAsync(
                        _permissionModule,
                        token);
                    var permanentDelete =
                        _authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.PermanentDelete);
                    var mutation = permissions.CanView && permanentDelete
                        ? await DeleteOnBackendAsync(item.Id, token)
                        : PartyMutationResult.Failed(PartyFailureReason.NotAuthorized);
                    var page = mutation.Success && permissions.CanView
                        ? await GetPageFromBackendAsync(targetPage, PageSize, _appliedSearchText, token)
                        : null;
                    return new PartySavePayload(permissions, permanentDelete, mutation, page);
                },
                cancellation.Token);

            if (!CanApplyResult(identity, cancellation, version))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            ApplyPermissions(payload.Permissions, payload.PermanentDelete);
            if (!CanView)
            {
                ClearProtectedState($"Vous n'êtes plus autorisé à consulter les {_partyPluralLower}.");
                return;
            }

            if (!payload.Mutation.Success)
            {
                HandleDeleteFailure(payload.Mutation.FailureReason);
                return;
            }

            IsDeleteConfirmationVisible = false;
            PartyPendingDelete = null;
            DeleteErrorMessage = null;
            SuccessMessage = $"{_partyKindCapitalized} supprimé.";

            if (payload.Page is { Success: true })
            {
                ApplyPage(payload.Page);
            }
            else if (payload.Page is not null)
            {
                HandleListFailure(payload.Page.FailureReason, preserveSuccess: true);
            }
            else
            {
                ListErrorMessage =
                    $"Le {_partyKindLower} a été supprimé, mais l'actualisation de la liste a échoué.";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Expected during lifecycle cancellation.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} delete failed: {1}", PageTitle, exception);
            if (!_isStopped && IsDeleteConfirmationVisible)
            {
                DeleteErrorMessage = $"Impossible de supprimer le {_partyKindLower}.";
            }
        }
        finally
        {
            if (ReferenceEquals(_operationCancellation, cancellation))
            {
                _operationCancellation = null;
            }

            if (!_isStopped)
            {
                IsDeleting = false;
            }
        }
    }

    private void ApplyPermissions(
        EffectiveModulePermission permissions,
        bool permanentDelete)
    {
        CanView = permissions.CanView;
        CanCreate = permissions.CanView && permissions.CanCreate;
        CanModify = permissions.CanView && permissions.CanModify;
        CanDelete = permissions.CanView && permanentDelete;
    }

    private void ApplyPage(PartyPageResult result)
    {
        if (!result.Success)
        {
            HandleListFailure(result.FailureReason);
            return;
        }

        Items.Clear();
        foreach (var item in result.Items)
        {
            Items.Add(item);
        }

        PageNumber = result.PageNumber;
        TotalPages = result.TotalPages;
        TotalCount = result.TotalCount;
        HasLoadedList = true;
        ListErrorMessage = null;
        NotifyItemsChanged();
    }

    private void HandleListFailure(PartyFailureReason failureReason, bool preserveSuccess = false)
    {
        if (!preserveSuccess)
        {
            SuccessMessage = null;
        }

        if (failureReason == PartyFailureReason.NotAuthenticated)
        {
            ClearProtectedState("Votre session n'est plus active.", preserveSuccess);
            return;
        }

        if (failureReason is PartyFailureReason.Forbidden or PartyFailureReason.NotAuthorized)
        {
            ClearProtectedState(
                $"Vous n'êtes pas autorisé à consulter les {_partyPluralLower}.",
                preserveSuccess);
            return;
        }

        ListErrorMessage = failureReason switch
        {
            PartyFailureReason.InvalidPageNumber => "La page demandée n'est pas disponible.",
            PartyFailureReason.InvalidPageSize => "La taille de page demandée n'est pas valide.",
            _ => GetListLoadError()
        };
    }

    private void HandleDetailFailure(PartyFailureReason failureReason)
    {
        if (failureReason == PartyFailureReason.NotAuthenticated)
        {
            ClearProtectedState("Votre session n'est plus active.");
            return;
        }

        if (failureReason is PartyFailureReason.Forbidden or PartyFailureReason.NotAuthorized)
        {
            CanModify = false;
            ListErrorMessage = $"Vous n'êtes plus autorisé à modifier ce {_partyKindLower}.";
            return;
        }

        ListErrorMessage = failureReason switch
        {
            PartyFailureReason.InvalidId => $"Le {_partyKindLower} sélectionné n'est pas valide.",
            PartyFailureReason.NotFound => $"Ce {_partyKindLower} n'existe plus.",
            _ => $"Impossible de charger le détail du {_partyKindLower}."
        };
    }

    private void HandleMutationFailure(PartyFailureReason failureReason, bool editMode)
    {
        if (failureReason == PartyFailureReason.NotAuthenticated)
        {
            ClearProtectedState("Votre session n'est plus active.");
            return;
        }

        if (failureReason is PartyFailureReason.Forbidden or PartyFailureReason.NotAuthorized)
        {
            if (editMode)
            {
                CanModify = false;
            }
            else
            {
                CanCreate = false;
            }

            ResetForm();
            IsFormVisible = false;
            ListErrorMessage = editMode
                ? $"Vous n'êtes plus autorisé à modifier un {_partyKindLower}."
                : $"Vous n'êtes plus autorisé à créer un {_partyKindLower}.";
            return;
        }

        FormErrorMessage = failureReason switch
        {
            PartyFailureReason.InvalidName => "Le nom est obligatoire.",
            PartyFailureReason.InvalidId => $"Le {_partyKindLower} sélectionné n'est pas valide.",
            PartyFailureReason.NotFound => $"Ce {_partyKindLower} n'existe plus.",
            _ => $"Impossible d'enregistrer le {_partyKindLower}."
        };
    }

    private void HandleDeleteFailure(PartyFailureReason failureReason)
    {
        if (failureReason == PartyFailureReason.NotAuthenticated)
        {
            ClearProtectedState("Votre session n'est plus active.");
            return;
        }

        if (failureReason is PartyFailureReason.Forbidden or PartyFailureReason.NotAuthorized)
        {
            CanDelete = false;
            CancelDelete();
            ListErrorMessage = $"Vous n'êtes plus autorisé à supprimer définitivement un {_partyKindLower}.";
            return;
        }

        DeleteErrorMessage = failureReason switch
        {
            PartyFailureReason.InvalidId => $"Le {_partyKindLower} sélectionné n'est pas valide.",
            PartyFailureReason.NotFound => $"Ce {_partyKindLower} n'existe plus.",
            PartyFailureReason.InUse =>
                $"Ce {_partyKindLower} ne peut pas être supprimé car il est déjà lié à des opérations financières.",
            _ => $"Impossible de supprimer le {_partyKindLower}."
        };
    }

    private bool TryCreateFormData(out PartyFormData form)
    {
        form = null!;
        var normalizedName = Name.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            FormErrorMessage = "Le nom est obligatoire.";
            return false;
        }

        form = new PartyFormData(
            normalizedName,
            NormalizeOptional(Phone),
            NormalizeOptional(Address),
            NormalizeOptional(Company),
            NormalizeOptional(Identifier),
            NormalizeOptional(Observation));
        return true;
    }

    private void PopulateForm(PartyDetailData item)
    {
        Name = item.Name;
        Phone = item.Phone ?? string.Empty;
        Address = item.Address ?? string.Empty;
        Company = item.Company ?? string.Empty;
        Identifier = item.Identifier ?? string.Empty;
        Observation = item.Observation ?? string.Empty;
        FormErrorMessage = null;
    }

    private void ResetForm()
    {
        _editingId = null;
        IsEditMode = false;
        Name = string.Empty;
        Phone = string.Empty;
        Address = string.Empty;
        Company = string.Empty;
        Identifier = string.Empty;
        Observation = string.Empty;
        FormErrorMessage = null;
    }

    private void ClearItems()
    {
        Items.Clear();
        PageNumber = 1;
        TotalPages = 0;
        TotalCount = 0;
        HasLoadedList = false;
        NotifyItemsChanged();
    }

    private void ClearProtectedState(string? message, bool preserveSuccess = false)
    {
        ClearItems();
        ResetForm();
        IsFormVisible = false;
        IsDeleteConfirmationVisible = false;
        PartyPendingDelete = null;
        DeleteErrorMessage = null;
        CanView = false;
        CanCreate = false;
        CanModify = false;
        CanDelete = false;
        ListErrorMessage = message;
        if (!preserveSuccess)
        {
            SuccessMessage = null;
        }
    }

    private void FailClosedIfSessionChanged(SessionIdentity identity)
    {
        if (!_isStopped && !IsSameAuthenticatedSession(identity))
        {
            ClearProtectedState("Votre session n'est plus active.");
        }
    }

    private void ClearFormError()
    {
        if (FormErrorMessage == "Le nom est obligatoire.")
        {
            FormErrorMessage = null;
        }
    }

    private void NotifyItemsChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowEmptyState));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(ShowEmptyState));
        RaiseCommandCanExecuteChanged();
    }

    private void RaiseCommandCanExecuteChanged()
    {
        _refreshRelayCommand.RaiseCanExecuteChanged();
        _applySearchRelayCommand.RaiseCanExecuteChanged();
        _resetSearchRelayCommand.RaiseCanExecuteChanged();
        _previousPageRelayCommand.RaiseCanExecuteChanged();
        _nextPageRelayCommand.RaiseCanExecuteChanged();
        _showCreateFormRelayCommand.RaiseCanExecuteChanged();
        _editRelayCommand.RaiseCanExecuteChanged();
        _cancelFormRelayCommand.RaiseCanExecuteChanged();
        _saveRelayCommand.RaiseCanExecuteChanged();
        _requestDeleteRelayCommand.RaiseCanExecuteChanged();
        _cancelDeleteRelayCommand.RaiseCanExecuteChanged();
        _confirmDeleteRelayCommand.RaiseCanExecuteChanged();
    }

    private bool CanApplyResult(
        SessionIdentity identity,
        CancellationTokenSource cancellation,
        long version) =>
        !_isStopped
        && !cancellation.IsCancellationRequested
        && version == Volatile.Read(ref _requestVersion)
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

    private string GetListLoadError() => $"Impossible de charger les {_partyPluralLower}.";

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private readonly record struct SessionIdentity(
        long UserId,
        string? Username,
        UserRole Role);
}

public enum PartyFailureReason
{
    None,
    NotAuthenticated,
    NotAuthorized,
    Forbidden,
    InvalidId,
    InvalidName,
    NotFound,
    InUse,
    InvalidPageNumber,
    InvalidPageSize,
    Technical
}

public sealed record PartyFormData(
    string Name,
    string? Phone,
    string? Address,
    string? Company,
    string? Identifier,
    string? Observation);

public sealed record PartyDetailData(
    long Id,
    string Name,
    string? Phone,
    string? Address,
    string? Company,
    string? Identifier,
    string? Observation);

public sealed record PartyPageResult(
    bool Success,
    PartyFailureReason FailureReason,
    IReadOnlyList<PartyListItemViewModel> Items,
    int PageNumber,
    int TotalPages,
    int TotalCount)
{
    public static PartyPageResult Failed(PartyFailureReason reason, int pageNumber = 1) =>
        new(false, reason, Array.Empty<PartyListItemViewModel>(), pageNumber, 0, 0);
}

public sealed record PartyDetailResult(
    bool Success,
    PartyFailureReason FailureReason,
    PartyDetailData? Item)
{
    public static PartyDetailResult Failed(PartyFailureReason reason) =>
        new(false, reason, null);
}

public sealed record PartyMutationResult(
    bool Success,
    PartyFailureReason FailureReason)
{
    public static PartyMutationResult Succeeded() => new(true, PartyFailureReason.None);

    public static PartyMutationResult Failed(PartyFailureReason reason) => new(false, reason);
}

internal sealed record PartyInitializationPayload(
    EffectiveModulePermission Permissions,
    bool PermanentDelete,
    PartyPageResult? Page);

internal sealed record PartyDetailPayload(
    EffectiveModulePermission Permissions,
    bool PermanentDelete,
    PartyDetailResult Detail);

internal sealed record PartySavePayload(
    EffectiveModulePermission Permissions,
    bool PermanentDelete,
    PartyMutationResult Mutation,
    PartyPageResult? Page);
