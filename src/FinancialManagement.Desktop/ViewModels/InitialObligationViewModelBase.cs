using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public abstract class InitialObligationViewModelBase : ViewModelBase
{
    private const int PageSize = 50;
    private const int PartyLookupLimit = 20;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly AsyncRelayCommand _refreshRelayCommand;
    private readonly AsyncRelayCommand _previousPageRelayCommand;
    private readonly AsyncRelayCommand _nextPageRelayCommand;
    private readonly AsyncRelayCommand _showCreateRelayCommand;
    private readonly AsyncRelayCommand _searchFilterPartiesRelayCommand;
    private readonly AsyncRelayCommand _applyPartyFilterRelayCommand;
    private readonly AsyncRelayCommand _clearPartyFilterRelayCommand;
    private readonly RelayCommand _cancelCreateRelayCommand;
    private readonly AsyncRelayCommand _searchPartiesRelayCommand;
    private readonly RelayCommand _selectPartyRelayCommand;
    private readonly AsyncRelayCommand _saveRelayCommand;
    private CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _operationCancellation;
    private Task _pendingOperation = Task.CompletedTask;
    private long _requestVersion;
    private int _pageNumber = 1;
    private int _totalPages;
    private int _totalCount;
    private bool _hasLoaded;
    private bool _isLoading;
    private bool _isLookupLoading;
    private bool _isSaving;
    private bool _isCreateVisible;
    private bool _isStopped = true;
    private string _partySearchText = string.Empty;
    private string _filterPartySearchText = string.Empty;
    private InitialObligationPartyItemViewModel? _selectedFilterPartyResult;
    private long? _appliedPartyId;
    private InitialObligationPartyItemViewModel? _selectedPartyResult;
    private InitialObligationPartyItemViewModel? _selectedParty;
    private string _amount = string.Empty;
    private DateTime? _businessDate = DateTime.Today;
    private DateTime? _dueDate;
    private string _observation = string.Empty;
    private string? _listErrorMessage;
    private string? _formErrorMessage;
    private string? _successMessage;

    protected InitialObligationViewModelBase(
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator,
        string title,
        string description,
        string partyLabel,
        string dateLabel,
        string emptyMessage)
    {
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);
        _currentUserSession = currentUserSession;
        _authorizationService = authorizationService;
        _operationCoordinator = operationCoordinator;
        Title = title;
        Description = description;
        PartyLabel = partyLabel;
        DateLabel = dateLabel;
        EmptyMessage = emptyMessage;

        _refreshRelayCommand = new AsyncRelayCommand(
            _ => Track(LoadPageAsync(PageNumber)),
            _ => CanLoad);
        _previousPageRelayCommand = new AsyncRelayCommand(
            _ => Track(LoadPageAsync(PageNumber - 1)),
            _ => CanLoad && PageNumber > 1);
        _nextPageRelayCommand = new AsyncRelayCommand(
            _ => Track(LoadPageAsync(PageNumber + 1)),
            _ => CanLoad && TotalPages > 0 && PageNumber < TotalPages);
        _showCreateRelayCommand = new AsyncRelayCommand(
            _ => Track(ShowCreateAsync()),
            _ => CanLoad && !IsCreateVisible);
        _searchFilterPartiesRelayCommand = new AsyncRelayCommand(
            _ => Track(SearchFilterPartiesAsync()),
            _ => CanLoad);
        _applyPartyFilterRelayCommand = new AsyncRelayCommand(
            _ => Track(ApplyPartyFilterAsync()),
            _ => CanLoad && SelectedFilterPartyResult is not null);
        _clearPartyFilterRelayCommand = new AsyncRelayCommand(
            _ => Track(ClearPartyFilterAsync()),
            _ => CanLoad && (_appliedPartyId.HasValue || SelectedFilterPartyResult is not null));
        _cancelCreateRelayCommand = new RelayCommand(
            _ => CloseCreate(),
            _ => IsCreateVisible && !IsSaving);
        _searchPartiesRelayCommand = new AsyncRelayCommand(
            _ => Track(SearchPartiesCoreAsync()),
            _ => IsCreateVisible && !IsBusy);
        _selectPartyRelayCommand = new RelayCommand(
            _ => SelectParty(),
            _ => IsCreateVisible && SelectedPartyResult is not null && !IsBusy);
        _saveRelayCommand = new AsyncRelayCommand(
            _ => Track(SaveAsync()),
            _ => IsCreateVisible && SelectedParty is not null && !IsBusy);

        RefreshCommand = _refreshRelayCommand;
        PreviousPageCommand = _previousPageRelayCommand;
        NextPageCommand = _nextPageRelayCommand;
        ShowCreateCommand = _showCreateRelayCommand;
        SearchFilterPartiesCommand = _searchFilterPartiesRelayCommand;
        ApplyPartyFilterCommand = _applyPartyFilterRelayCommand;
        ClearPartyFilterCommand = _clearPartyFilterRelayCommand;
        CancelCreateCommand = _cancelCreateRelayCommand;
        SearchPartiesCommand = _searchPartiesRelayCommand;
        SelectPartyCommand = _selectPartyRelayCommand;
        SaveCommand = _saveRelayCommand;
    }

    public event EventHandler? AuthenticationInvalidated;

    public ObservableCollection<InitialObligationListItemViewModel> Items { get; } = [];

    public ObservableCollection<InitialObligationPartyItemViewModel> PartyResults { get; } = [];

    public ObservableCollection<InitialObligationPartyItemViewModel> FilterPartyResults { get; } = [];

    public ICommand RefreshCommand { get; }

    public ICommand PreviousPageCommand { get; }

    public ICommand NextPageCommand { get; }

    public ICommand ShowCreateCommand { get; }

    public ICommand SearchFilterPartiesCommand { get; }

    public ICommand ApplyPartyFilterCommand { get; }

    public ICommand ClearPartyFilterCommand { get; }

    public ICommand CancelCreateCommand { get; }

    public ICommand SearchPartiesCommand { get; }

    public ICommand SelectPartyCommand { get; }

    public ICommand SaveCommand { get; }

    public string Title { get; }

    public string Description { get; }

    public string PartyLabel { get; }

    public string DateLabel { get; }

    public string EmptyMessage { get; }

    public string NewItemLabel => $"Ajouter {Title.ToLowerInvariant()}";

    public string SearchPartyLabel => $"Rechercher un {PartyLabel.ToLowerInvariant()}";

    public string FilterPartySearchText
    {
        get => _filterPartySearchText;
        set => SetProperty(ref _filterPartySearchText, value);
    }

    public InitialObligationPartyItemViewModel? SelectedFilterPartyResult
    {
        get => _selectedFilterPartyResult;
        set
        {
            if (SetProperty(ref _selectedFilterPartyResult, value))
            {
                _applyPartyFilterRelayCommand.RaiseCanExecuteChanged();
                _clearPartyFilterRelayCommand.RaiseCanExecuteChanged();
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
                OnPropertyChanged(nameof(PageLabel));
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
                OnPropertyChanged(nameof(PageLabel));
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
                OnPropertyChanged(nameof(TotalLabel));
            }
        }
    }

    public string PageLabel => TotalPages == 0 ? "Page 0 / 0" : $"Page {PageNumber} / {TotalPages}";

    public string TotalLabel => $"{TotalCount} élément(s)";

    public bool HasItems => Items.Count > 0;

    public bool ShowEmptyState => HasLoaded && !IsLoading && Items.Count == 0;

    public bool HasLoaded
    {
        get => _hasLoaded;
        private set
        {
            if (SetProperty(ref _hasLoaded, value))
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
                NotifyBusyChanged();
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
    }

    public bool IsLookupLoading
    {
        get => _isLookupLoading;
        private set
        {
            if (SetProperty(ref _isLookupLoading, value))
            {
                NotifyBusyChanged();
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
                NotifyBusyChanged();
            }
        }
    }

    public bool IsBusy => IsLoading || IsLookupLoading || IsSaving;

    public bool IsCreateVisible
    {
        get => _isCreateVisible;
        private set
        {
            if (SetProperty(ref _isCreateVisible, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string PartySearchText
    {
        get => _partySearchText;
        set => SetProperty(ref _partySearchText, value);
    }

    public InitialObligationPartyItemViewModel? SelectedPartyResult
    {
        get => _selectedPartyResult;
        set
        {
            if (SetProperty(ref _selectedPartyResult, value))
            {
                _selectPartyRelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public InitialObligationPartyItemViewModel? SelectedParty
    {
        get => _selectedParty;
        private set
        {
            if (SetProperty(ref _selectedParty, value))
            {
                OnPropertyChanged(nameof(SelectedPartyLabel));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string SelectedPartyLabel => SelectedParty?.DisplayLabel ?? $"Aucun {PartyLabel.ToLowerInvariant()} sélectionné";

    public string Amount
    {
        get => _amount;
        set
        {
            if (SetProperty(ref _amount, value))
            {
                FormErrorMessage = null;
            }
        }
    }

    public DateTime? BusinessDate
    {
        get => _businessDate;
        set => SetProperty(ref _businessDate, value);
    }

    public DateTime? DueDate
    {
        get => _dueDate;
        set => SetProperty(ref _dueDate, value);
    }

    public string Observation
    {
        get => _observation;
        set => SetProperty(ref _observation, value);
    }

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

    public string? SuccessMessage
    {
        get => _successMessage;
        private set
        {
            if (SetProperty(ref _successMessage, value))
            {
                OnPropertyChanged(nameof(HasSuccess));
            }
        }
    }

    public bool HasSuccess => !string.IsNullOrWhiteSpace(SuccessMessage);

    public bool CanLoad => !_isStopped && !IsBusy && IsResponsibleSession();

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Reactivate();
        return HasLoaded ? Task.CompletedTask : Track(LoadPageAsync(1, cancellationToken));
    }

    public void Deactivate()
    {
        _isStopped = true;
        Interlocked.Increment(ref _requestVersion);
        _operationCancellation?.Cancel();
        _lifetimeCancellation.Cancel();
        CloseCreate();
        ClearProtectedData();
        RaiseCommandCanExecuteChanged();
    }

    public async Task StopAsync()
    {
        Deactivate();
        try
        {
            await _pendingOperation;
        }
        catch (OperationCanceledException)
        {
        }
    }

    protected abstract Task<InitialObligationPageResponse> GetPageAsync(
        int pageNumber,
        int pageSize,
        long? partyId,
        CancellationToken cancellationToken);

    protected abstract Task<InitialObligationPartyLookupResponse> SearchPartiesAsync(
        string? searchText,
        int limit,
        CancellationToken cancellationToken);

    protected abstract Task<InitialObligationCreateResponse> CreateAsync(
        long partyId,
        long amountMinor,
        DateOnly businessDate,
        DateOnly? dueDate,
        string? observation,
        CancellationToken cancellationToken);

    private async Task LoadPageAsync(int pageNumber, CancellationToken cancellationToken = default)
    {
        if (!TryCaptureSession(out var identity) || _isStopped || pageNumber < 1)
        {
            FailClosed();
            return;
        }

        var version = BeginOperation(cancellationToken, out var linkedCancellation);
        IsLoading = true;
        ListErrorMessage = null;
        try
        {
            using (linkedCancellation)
            {
                var response = await _operationCoordinator.RunAsync(
                    token => GetPageAsync(pageNumber, PageSize, _appliedPartyId, token),
                    linkedCancellation.Token);
                if (!CanAcceptResult(version, identity))
                {
                    return;
                }

                if (!response.Success)
                {
                    if (response.AuthenticationFailure)
                    {
                        FailClosed();
                        return;
                    }

                    ListErrorMessage = response.ErrorMessage ?? "Impossible de charger les éléments initiaux.";
                    return;
                }

                Items.Clear();
                foreach (var item in response.Items)
                {
                    Items.Add(item);
                }

                PageNumber = response.PageNumber;
                TotalPages = response.TotalPages;
                TotalCount = response.TotalCount;
                HasLoaded = true;
                OnPropertyChanged(nameof(HasItems));
                OnPropertyChanged(nameof(ShowEmptyState));
            }
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Initial obligation list failed unexpectedly: {0}", exception);
            if (CanAcceptResult(version, identity))
            {
                ListErrorMessage = "Impossible de charger les éléments initiaux.";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task ShowCreateAsync()
    {
        ResetForm();
        IsCreateVisible = true;
        await SearchPartiesCoreAsync();
    }

    private async Task SearchFilterPartiesAsync()
    {
        if (!TryCaptureSession(out var identity) || _isStopped)
        {
            FailClosed();
            return;
        }

        var version = BeginOperation(default, out var linkedCancellation);
        IsLookupLoading = true;
        ListErrorMessage = null;
        try
        {
            using (linkedCancellation)
            {
                var response = await _operationCoordinator.RunAsync(
                    token => SearchPartiesAsync(
                        string.IsNullOrWhiteSpace(FilterPartySearchText) ? null : FilterPartySearchText.Trim(),
                        PartyLookupLimit,
                        token),
                    linkedCancellation.Token);
                if (!CanAcceptResult(version, identity))
                {
                    return;
                }

                if (!response.Success)
                {
                    if (response.AuthenticationFailure)
                    {
                        FailClosed();
                        return;
                    }

                    ListErrorMessage = response.ErrorMessage ?? "Impossible de rechercher le tiers.";
                    return;
                }

                FilterPartyResults.Clear();
                foreach (var item in response.Items)
                {
                    FilterPartyResults.Add(item);
                }

                SelectedFilterPartyResult = FilterPartyResults.FirstOrDefault();
                if (FilterPartyResults.Count == 0)
                {
                    ListErrorMessage = "Aucun tiers ne correspond à la recherche.";
                }
            }
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Initial obligation filter lookup failed unexpectedly: {0}", exception);
            if (CanAcceptResult(version, identity))
            {
                ListErrorMessage = "Impossible de rechercher le tiers.";
            }
        }
        finally
        {
            IsLookupLoading = false;
        }
    }

    private async Task ApplyPartyFilterAsync()
    {
        if (SelectedFilterPartyResult is null)
        {
            return;
        }

        _appliedPartyId = SelectedFilterPartyResult.Id;
        _clearPartyFilterRelayCommand.RaiseCanExecuteChanged();
        HasLoaded = false;
        await LoadPageAsync(1);
    }

    private async Task ClearPartyFilterAsync()
    {
        _appliedPartyId = null;
        FilterPartySearchText = string.Empty;
        FilterPartyResults.Clear();
        SelectedFilterPartyResult = null;
        HasLoaded = false;
        await LoadPageAsync(1);
    }

    private async Task SearchPartiesCoreAsync()
    {
        if (!TryCaptureSession(out var identity) || _isStopped || !IsCreateVisible)
        {
            FailClosed();
            return;
        }

        var version = BeginOperation(default, out var linkedCancellation);
        IsLookupLoading = true;
        FormErrorMessage = null;
        try
        {
            using (linkedCancellation)
            {
                var response = await _operationCoordinator.RunAsync(
                    token => SearchPartiesAsync(
                        string.IsNullOrWhiteSpace(PartySearchText) ? null : PartySearchText.Trim(),
                        PartyLookupLimit,
                        token),
                    linkedCancellation.Token);
                if (!CanAcceptResult(version, identity))
                {
                    return;
                }

                if (!response.Success)
                {
                    if (response.AuthenticationFailure)
                    {
                        FailClosed();
                        return;
                    }

                    FormErrorMessage = response.ErrorMessage ?? $"Impossible de rechercher les {PartyLabel.ToLowerInvariant()}s.";
                    return;
                }

                PartyResults.Clear();
                foreach (var item in response.Items)
                {
                    PartyResults.Add(item);
                }

                SelectedPartyResult = PartyResults.FirstOrDefault();
                if (PartyResults.Count == 0)
                {
                    FormErrorMessage = $"Aucun {PartyLabel.ToLowerInvariant()} trouvé.";
                }
            }
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Initial obligation party lookup failed unexpectedly: {0}", exception);
            if (CanAcceptResult(version, identity))
            {
                FormErrorMessage = $"Impossible de rechercher les {PartyLabel.ToLowerInvariant()}s.";
            }
        }
        finally
        {
            IsLookupLoading = false;
        }
    }

    private void SelectParty()
    {
        SelectedParty = SelectedPartyResult;
        FormErrorMessage = null;
    }

    private async Task SaveAsync()
    {
        if (!TryCaptureSession(out var identity) || _isStopped || SelectedParty is null)
        {
            FailClosed();
            return;
        }

        FormErrorMessage = null;
        if (!MoneyParser.TryParsePositiveMinorUnits(Amount, out var amountMinor))
        {
            FormErrorMessage = "Le montant initial doit être strictement positif.";
            return;
        }

        if (BusinessDate is not DateTime businessDate)
        {
            FormErrorMessage = $"La {DateLabel.ToLowerInvariant()} est obligatoire.";
            return;
        }

        var selectedPartyId = SelectedParty.Id;
        var version = BeginOperation(default, out var linkedCancellation);
        IsSaving = true;
        try
        {
            using (linkedCancellation)
            {
                var response = await _operationCoordinator.RunAsync(
                    token => CreateAsync(
                        selectedPartyId,
                        amountMinor,
                        DateOnly.FromDateTime(businessDate),
                        DueDate is DateTime dueDate ? DateOnly.FromDateTime(dueDate) : null,
                        string.IsNullOrWhiteSpace(Observation) ? null : Observation.Trim(),
                        token),
                    linkedCancellation.Token);
                if (!CanAcceptResult(version, identity))
                {
                    return;
                }

                if (!response.Success)
                {
                    if (response.AuthenticationFailure)
                    {
                        FailClosed();
                        return;
                    }

                    FormErrorMessage = response.ErrorMessage ?? "Impossible d'enregistrer l'élément initial.";
                    return;
                }

                CloseCreate();
                SuccessMessage = string.IsNullOrWhiteSpace(response.Reference)
                    ? "L'élément initial a été enregistré."
                    : $"L'élément initial {response.Reference} a été enregistré.";
                HasLoaded = false;
                await LoadPageAsync(1);
            }
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Initial obligation creation failed unexpectedly: {0}", exception);
            if (CanAcceptResult(version, identity))
            {
                FormErrorMessage = "Impossible d'enregistrer l'élément initial.";
            }
        }
        finally
        {
            IsSaving = false;
        }
    }

    private long BeginOperation(CancellationToken cancellationToken, out CancellationTokenSource linkedCancellation)
    {
        _operationCancellation?.Cancel();
        _operationCancellation?.Dispose();
        _operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(_operationCancellation.Token);
        return Interlocked.Increment(ref _requestVersion);
    }

    private void Reactivate()
    {
        if (!_isStopped)
        {
            return;
        }

        _lifetimeCancellation.Dispose();
        _lifetimeCancellation = new CancellationTokenSource();
        _isStopped = false;
        RaiseCommandCanExecuteChanged();
    }

    private bool CanAcceptResult(long version, SessionIdentity identity) =>
        !_isStopped
        && version == Volatile.Read(ref _requestVersion)
        && IsSameSession(identity);

    private bool TryCaptureSession(out SessionIdentity identity)
    {
        if (IsResponsibleSession() && _currentUserSession.UserId is long userId)
        {
            identity = new SessionIdentity(userId, _currentUserSession.Username);
            return true;
        }

        identity = default;
        return false;
    }

    private bool IsResponsibleSession() =>
        _currentUserSession.IsAuthenticated
        && _currentUserSession.Role == UserRole.Responsable
        && _authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.Administration);

    private bool IsSameSession(SessionIdentity identity) =>
        IsResponsibleSession()
        && _currentUserSession.UserId == identity.UserId
        && string.Equals(_currentUserSession.Username, identity.Username, StringComparison.Ordinal);

    private void FailClosed()
    {
        CloseCreate();
        ClearProtectedData();
        SuccessMessage = null;
        ListErrorMessage = "Votre session n'autorise plus cette opération.";
        AuthenticationInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void ClearProtectedData()
    {
        Items.Clear();
        PartyResults.Clear();
        FilterPartyResults.Clear();
        SelectedFilterPartyResult = null;
        _appliedPartyId = null;
        FilterPartySearchText = string.Empty;
        SelectedParty = null;
        SelectedPartyResult = null;
        HasLoaded = false;
        PageNumber = 1;
        TotalPages = 0;
        TotalCount = 0;
        SuccessMessage = null;
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void ResetForm()
    {
        PartySearchText = string.Empty;
        PartyResults.Clear();
        SelectedPartyResult = null;
        SelectedParty = null;
        Amount = string.Empty;
        BusinessDate = DateTime.Today;
        DueDate = null;
        Observation = string.Empty;
        FormErrorMessage = null;
        SuccessMessage = null;
    }

    private void CloseCreate()
    {
        IsCreateVisible = false;
        ResetForm();
    }

    private void NotifyBusyChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        RaiseCommandCanExecuteChanged();
    }

    private void RaiseCommandCanExecuteChanged()
    {
        _refreshRelayCommand.RaiseCanExecuteChanged();
        _previousPageRelayCommand.RaiseCanExecuteChanged();
        _nextPageRelayCommand.RaiseCanExecuteChanged();
        _showCreateRelayCommand.RaiseCanExecuteChanged();
        _searchFilterPartiesRelayCommand.RaiseCanExecuteChanged();
        _applyPartyFilterRelayCommand.RaiseCanExecuteChanged();
        _clearPartyFilterRelayCommand.RaiseCanExecuteChanged();
        _cancelCreateRelayCommand.RaiseCanExecuteChanged();
        _searchPartiesRelayCommand.RaiseCanExecuteChanged();
        _selectPartyRelayCommand.RaiseCanExecuteChanged();
        _saveRelayCommand.RaiseCanExecuteChanged();
    }

    private Task Track(Task operation)
    {
        _pendingOperation = operation;
        return operation;
    }

    private readonly record struct SessionIdentity(long UserId, string? Username);
}
