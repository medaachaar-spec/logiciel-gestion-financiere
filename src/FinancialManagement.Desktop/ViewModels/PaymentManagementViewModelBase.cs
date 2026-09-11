using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Payments;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public abstract class PaymentManagementViewModelBase : ViewModelBase
{
    private const int PageSize = 50;
    private const int PartyLookupLimit = 20;
    private const int ObligationLookupLimit = 50;
    private const string InvalidDateRangeMessage =
        "La date de début doit être antérieure ou égale à la date de fin.";
    private const string InvalidAmountMessage =
        "Saisissez un montant strictement positif avec au maximum deux décimales.";

    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly PermissionModule _paymentModule;
    private readonly PermissionModule _partyModule;
    private readonly string _partyKindLower;
    private readonly string _partyKindCapitalized;
    private readonly string _paymentPluralLower;
    private readonly AsyncRelayCommand _refreshRelayCommand;
    private readonly AsyncRelayCommand _applyFiltersRelayCommand;
    private readonly AsyncRelayCommand _resetFiltersRelayCommand;
    private readonly AsyncRelayCommand _previousPageRelayCommand;
    private readonly AsyncRelayCommand _nextPageRelayCommand;
    private readonly AsyncRelayCommand _openDetailRelayCommand;
    private readonly RelayCommand _showCreateForObligationRelayCommand;
    private readonly RelayCommand _closeDetailRelayCommand;
    private readonly RelayCommand _showCreateRelayCommand;
    private readonly RelayCommand _cancelCreateRelayCommand;
    private readonly AsyncRelayCommand _searchPartiesRelayCommand;
    private readonly AsyncRelayCommand _selectPartyRelayCommand;
    private readonly RelayCommand _clearSelectedPartyRelayCommand;
    private readonly AsyncRelayCommand _searchObligationsRelayCommand;
    private readonly RelayCommand _prepareCreateRelayCommand;
    private readonly RelayCommand _cancelCreateConfirmationRelayCommand;
    private readonly AsyncRelayCommand _confirmCreateRelayCommand;
    private readonly AsyncRelayCommand _showAdvanceRelayCommand;
    private readonly RelayCommand _cancelAdvanceRelayCommand;
    private readonly AsyncRelayCommand _applyAdvanceRelayCommand;
    private string _searchText = string.Empty;
    private DateTime? _fromDate;
    private DateTime? _toDate;
    private PaymentStatusFilterOptionViewModel? _selectedFilterStatus;
    private string? _appliedSearchText;
    private DateOnly? _appliedFromDate;
    private DateOnly? _appliedToDate;
    private PaymentObligationStatus? _appliedStatus;
    private int _pageNumber = 1;
    private int _totalPages;
    private int _totalCount;
    private bool _hasLoadedList;
    private bool _isLoading;
    private bool _isLoadingDetail;
    private bool _isLookingUpParties;
    private bool _isLoadingObligations;
    private bool _isSaving;
    private bool _isApplyingAdvance;
    private bool _permissionsLoaded;
    private bool _canViewPayments;
    private bool _canCreatePayment;
    private bool _canApplyAdvancePermission;
    private bool _isCreateVisible;
    private bool _isCreateConfirmationVisible;
    private bool _isDetailVisible;
    private bool _isAdvanceVisible;
    private string? _listErrorMessage;
    private string? _workflowErrorMessage;
    private string? _successMessage;
    private string _partySearchText = string.Empty;
    private PaymentPartyLookupItemViewModel? _selectedPartyResult;
    private PaymentPartyLookupItemViewModel? _selectedParty;
    private string _obligationSearchText = string.Empty;
    private string _paymentAmount = string.Empty;
    private string _observation = string.Empty;
    private PaymentMethodOptionViewModel? _selectedPaymentMethod;
    private PaymentDetailData? _selectedDetail;
    private PaymentObligationDetailData? _selectedObligationDetail;
    private string _detailReference = string.Empty;
    private string _detailDate = string.Empty;
    private string _detailParty = string.Empty;
    private string _detailAmount = string.Empty;
    private string _detailAllocatedAmount = string.Empty;
    private string _detailUnallocatedAmount = string.Empty;
    private string _detailPaymentMethod = string.Empty;
    private string _detailObservation = string.Empty;
    private string _detailTargetType = string.Empty;
    private string _detailPaidAmount = string.Empty;
    private string _detailRemainingAmount = string.Empty;
    private string _detailStatus = string.Empty;
    private bool _isTargetObligationLocked;
    private string _allocatedSummary = MoneyFormatter.FormatMinor(0);
    private string _remainingSummary = MoneyFormatter.FormatMinor(0);
    private string _confirmationParty = string.Empty;
    private string _confirmationAmount = string.Empty;
    private string _confirmationAllocated = string.Empty;
    private string _confirmationAdvance = string.Empty;
    private string _confirmationPaymentMethod = string.Empty;
    private string _confirmationObligationCount = string.Empty;
    private long _preparedAmountMinor;
    private IReadOnlyList<PaymentAllocationRequest> _preparedAllocations = [];
    private long _advanceAvailableMinor;
    private bool _isActive;
    private bool _isStopped;
    private long _requestVersion;
    private CancellationTokenSource? _lifecycleCancellation;
    private Task _pendingOperation = Task.CompletedTask;

    protected PaymentManagementViewModelBase(
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator,
        PermissionModule paymentModule,
        PermissionModule partyModule,
        string pageTitle,
        string pageDescription,
        string partyKindLower,
        string partyKindCapitalized,
        string paymentPluralLower)
    {
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageDescription);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyKindLower);
        ArgumentException.ThrowIfNullOrWhiteSpace(partyKindCapitalized);
        ArgumentException.ThrowIfNullOrWhiteSpace(paymentPluralLower);

        _currentUserSession = currentUserSession;
        _authorizationService = authorizationService;
        _operationCoordinator = operationCoordinator;
        _paymentModule = paymentModule;
        _partyModule = partyModule;
        _partyKindLower = partyKindLower;
        _partyKindCapitalized = partyKindCapitalized;
        _paymentPluralLower = paymentPluralLower;
        PageTitle = pageTitle;
        PageDescription = pageDescription;

        PaymentMethods =
        [
            new PaymentMethodOptionViewModel(PaymentMethod.Cash, "Espèces"),
            new PaymentMethodOptionViewModel(PaymentMethod.BankTransfer, "Virement bancaire")
        ];
        FilterStatuses =
        [
            new PaymentStatusFilterOptionViewModel(null, "Toutes"),
            new PaymentStatusFilterOptionViewModel(PaymentObligationStatus.Outstanding, "À régler"),
            new PaymentStatusFilterOptionViewModel(PaymentObligationStatus.PartiallyPaid, "Partiellement réglées"),
            new PaymentStatusFilterOptionViewModel(PaymentObligationStatus.Settled, "Réglées")
        ];
        _selectedPaymentMethod = PaymentMethods[0];
        _selectedFilterStatus = FilterStatuses[0];

        _refreshRelayCommand = new AsyncRelayCommand(_ => RefreshAsync(), _ => CanLoadPage);
        _applyFiltersRelayCommand = new AsyncRelayCommand(_ => ApplyFiltersAsync(), _ => CanLoadPage);
        _resetFiltersRelayCommand = new AsyncRelayCommand(_ => ResetFiltersAsync(), _ => CanLoadPage);
        _previousPageRelayCommand = new AsyncRelayCommand(
            _ => GoToPreviousPageAsync(),
            _ => CanLoadPage && PageNumber > 1);
        _nextPageRelayCommand = new AsyncRelayCommand(
            _ => GoToNextPageAsync(),
            _ => CanLoadPage && TotalPages > 0 && PageNumber < TotalPages);
        _openDetailRelayCommand = new AsyncRelayCommand(
            parameter => Track(OpenDetailAsync(parameter as PaymentListItemViewModel)),
            parameter => CanLoadPage && parameter is PaymentListItemViewModel);
        _showCreateForObligationRelayCommand = new RelayCommand(
            parameter => ShowCreateForObligation(parameter as PaymentListItemViewModel),
            parameter => CanCreatePayment
                && !IsBusy
                && !HasOpenWorkflow
                && parameter is PaymentListItemViewModel { HasRemaining: true });
        _closeDetailRelayCommand = new RelayCommand(
            _ => CloseDetail(),
            _ => IsDetailVisible && !IsBusy);
        _showCreateRelayCommand = new RelayCommand(
            _ => ShowCreate(),
            _ => CanCreatePayment && !IsBusy && !HasOpenWorkflow);
        _cancelCreateRelayCommand = new RelayCommand(
            _ => CancelCreate(),
            _ => IsCreateVisible && !IsBusy);
        _searchPartiesRelayCommand = new AsyncRelayCommand(
            _ => Track(SearchPartiesAsync()),
            _ => IsCreateVisible && CanCreatePayment && !IsBusy);
        _selectPartyRelayCommand = new AsyncRelayCommand(
            _ => Track(SelectPartyAsync()),
            _ => IsCreateVisible && SelectedPartyResult is not null && !IsBusy);
        _clearSelectedPartyRelayCommand = new RelayCommand(
            _ => ClearSelectedParty(),
            _ => IsCreateVisible && SelectedParty is not null && CanChangeCreateTarget && !IsBusy);
        _searchObligationsRelayCommand = new AsyncRelayCommand(
            _ => SearchObligationsAsync(),
            _ => CanSearchObligations && !IsBusy);
        _prepareCreateRelayCommand = new RelayCommand(
            _ => PrepareCreate(),
            _ => IsCreateVisible && !IsBusy && !IsCreateConfirmationVisible);
        _cancelCreateConfirmationRelayCommand = new RelayCommand(
            _ => IsCreateConfirmationVisible = false,
            _ => IsCreateConfirmationVisible && !IsSaving);
        _confirmCreateRelayCommand = new AsyncRelayCommand(
            _ => Track(ConfirmCreateAsync()),
            _ => IsCreateConfirmationVisible && CanCreatePayment && !IsBusy);
        _showAdvanceRelayCommand = new AsyncRelayCommand(
            _ => Track(ShowAdvanceAsync()),
            _ => CanApplySelectedAdvance && !IsBusy);
        _cancelAdvanceRelayCommand = new RelayCommand(
            _ => CancelAdvance(),
            _ => IsAdvanceVisible && !IsBusy);
        _applyAdvanceRelayCommand = new AsyncRelayCommand(
            _ => Track(ApplyAdvanceAsync()),
            _ => IsAdvanceVisible && CanApplySelectedAdvance && !IsBusy);

        RefreshCommand = _refreshRelayCommand;
        ApplyFiltersCommand = _applyFiltersRelayCommand;
        ResetFiltersCommand = _resetFiltersRelayCommand;
        PreviousPageCommand = _previousPageRelayCommand;
        NextPageCommand = _nextPageRelayCommand;
        OpenDetailCommand = _openDetailRelayCommand;
        ShowCreateForObligationCommand = _showCreateForObligationRelayCommand;
        CloseDetailCommand = _closeDetailRelayCommand;
        ShowCreateCommand = _showCreateRelayCommand;
        CancelCreateCommand = _cancelCreateRelayCommand;
        SearchPartiesCommand = _searchPartiesRelayCommand;
        SelectPartyCommand = _selectPartyRelayCommand;
        ClearSelectedPartyCommand = _clearSelectedPartyRelayCommand;
        SearchObligationsCommand = _searchObligationsRelayCommand;
        PrepareCreateCommand = _prepareCreateRelayCommand;
        CancelCreateConfirmationCommand = _cancelCreateConfirmationRelayCommand;
        ConfirmCreateCommand = _confirmCreateRelayCommand;
        ShowAdvanceCommand = _showAdvanceRelayCommand;
        CancelAdvanceCommand = _cancelAdvanceRelayCommand;
        ApplyAdvanceCommand = _applyAdvanceRelayCommand;
    }

    public ObservableCollection<PaymentListItemViewModel> Payments { get; } = [];

    public ObservableCollection<PaymentPartyLookupItemViewModel> PartyResults { get; } = [];

    public ObservableCollection<PaymentObligationItemViewModel> Obligations { get; } = [];

    public ObservableCollection<PaymentAllocationDetailItemViewModel> DetailAllocations { get; } = [];

    public ObservableCollection<PaymentHistoryItemViewModel> DetailHistory { get; } = [];

    public IReadOnlyList<PaymentMethodOptionViewModel> PaymentMethods { get; }

    public IReadOnlyList<PaymentStatusFilterOptionViewModel> FilterStatuses { get; }

    public ICommand RefreshCommand { get; }
    public ICommand ApplyFiltersCommand { get; }
    public ICommand ResetFiltersCommand { get; }
    public ICommand PreviousPageCommand { get; }
    public ICommand NextPageCommand { get; }
    public ICommand OpenDetailCommand { get; }
    public ICommand ShowCreateForObligationCommand { get; }
    public ICommand CloseDetailCommand { get; }
    public ICommand ShowCreateCommand { get; }
    public ICommand CancelCreateCommand { get; }
    public ICommand SearchPartiesCommand { get; }
    public ICommand SelectPartyCommand { get; }
    public ICommand ClearSelectedPartyCommand { get; }
    public ICommand SearchObligationsCommand { get; }
    public ICommand PrepareCreateCommand { get; }
    public ICommand CancelCreateConfirmationCommand { get; }
    public ICommand ConfirmCreateCommand { get; }
    public ICommand ShowAdvanceCommand { get; }
    public ICommand CancelAdvanceCommand { get; }
    public ICommand ApplyAdvanceCommand { get; }

    public string PageTitle { get; }

    public string PageDescription { get; }

    public string PartyColumnHeader => _partyKindCapitalized;

    public string DetailPartyLabel => _partyKindCapitalized;

    public string NewPaymentLabel => $"Nouveau {SingularPaymentLower}";

    public string CreateInstruction => IsTargetObligationLocked
        ? "Le tiers et l'obligation sont présélectionnés. Ajustez uniquement le montant si nécessaire."
        : "Choisissez le tiers et affectez explicitement le paiement aux obligations.";

    public string PaymentActionLabel => _partyKindLower == "client" ? "Encaisser" : "Payer";

    public string ObligationReferenceHeader => _partyKindLower == "client"
        ? "Référence vente"
        : "Référence achat";

    public string DetailTitle => _partyKindLower == "client"
        ? "Détail de la créance"
        : "Détail de la dette";

    public string DetailHistoryTitle => _partyKindLower == "client"
        ? "HISTORIQUE DES ENCAISSEMENTS"
        : "HISTORIQUE DES PAIEMENTS";

    protected abstract string SingularPaymentLower { get; }

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

    public PaymentStatusFilterOptionViewModel? SelectedFilterStatus
    {
        get => _selectedFilterStatus;
        set => SetProperty(ref _selectedFilterStatus, value);
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

    public string PageNumberDescription => TotalPages == 0
        ? "Page 1 / 1"
        : $"Page {PageNumber} / {TotalPages}";

    public string TotalCountDescription => TotalCount switch
    {
        0 => "Aucune obligation",
        1 => "1 obligation",
        _ => $"{TotalCount} obligations"
    };

    public bool IsLoading
    {
        get => _isLoading;
        private set => SetBusyProperty(ref _isLoading, value);
    }

    public bool IsLoadingDetail
    {
        get => _isLoadingDetail;
        private set => SetBusyProperty(ref _isLoadingDetail, value);
    }

    public bool IsLookingUpParties
    {
        get => _isLookingUpParties;
        private set => SetBusyProperty(ref _isLookingUpParties, value);
    }

    public bool IsLoadingObligations
    {
        get => _isLoadingObligations;
        private set => SetBusyProperty(ref _isLoadingObligations, value);
    }

    public bool IsSaving
    {
        get => _isSaving;
        private set => SetBusyProperty(ref _isSaving, value);
    }

    public bool IsApplyingAdvance
    {
        get => _isApplyingAdvance;
        private set => SetBusyProperty(ref _isApplyingAdvance, value);
    }

    public bool IsBusy => IsLoading
        || IsLoadingDetail
        || IsLookingUpParties
        || IsLoadingObligations
        || IsSaving
        || IsApplyingAdvance;

    public bool HasPayments => Payments.Count > 0;

    public bool ShowEmptyState => HasLoadedList && !HasPayments && !IsLoading;

    public string EmptyStateMessage => HasActiveFilters
        ? "Aucune obligation ne correspond aux filtres."
        : "Aucune obligation trouvée.";

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
        || _appliedStatus.HasValue
        || !string.IsNullOrWhiteSpace(_appliedSearchText);

    public bool CanViewPayments
    {
        get => _canViewPayments;
        private set => SetProperty(ref _canViewPayments, value);
    }

    public bool CanCreatePayment
    {
        get => _canCreatePayment;
        private set
        {
            if (SetProperty(ref _canCreatePayment, value))
            {
                OnPropertyChanged(nameof(ShowCreateUnavailable));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool CanApplyAdvancePermission
    {
        get => _canApplyAdvancePermission;
        private set
        {
            if (SetProperty(ref _canApplyAdvancePermission, value))
            {
                OnPropertyChanged(nameof(CanApplySelectedAdvance));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool ShowCreateUnavailable => _permissionsLoaded && CanViewPayments && !CanCreatePayment;

    public string CreateUnavailableMessage =>
        $"La création nécessite les droits de création de paiement et de consultation des {_partyKindLower}s.";

    public bool IsCreateVisible
    {
        get => _isCreateVisible;
        private set
        {
            if (SetProperty(ref _isCreateVisible, value))
            {
                OnPropertyChanged(nameof(HasOpenWorkflow));
                OnPropertyChanged(nameof(IsMainEnabled));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool IsCreateConfirmationVisible
    {
        get => _isCreateConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isCreateConfirmationVisible, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool IsDetailVisible
    {
        get => _isDetailVisible;
        private set
        {
            if (SetProperty(ref _isDetailVisible, value))
            {
                OnPropertyChanged(nameof(HasOpenWorkflow));
                OnPropertyChanged(nameof(IsMainEnabled));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool IsAdvanceVisible
    {
        get => _isAdvanceVisible;
        private set
        {
            if (SetProperty(ref _isAdvanceVisible, value))
            {
                OnPropertyChanged(nameof(HasOpenWorkflow));
                OnPropertyChanged(nameof(IsMainEnabled));
                OnPropertyChanged(nameof(CanSearchObligations));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool HasOpenWorkflow => IsCreateVisible || IsDetailVisible || IsAdvanceVisible;

    public bool IsMainEnabled => !HasOpenWorkflow && !IsBusy;

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

    public bool HasWorkflowError => !string.IsNullOrWhiteSpace(WorkflowErrorMessage);

    public string? WorkflowErrorMessage
    {
        get => _workflowErrorMessage;
        private set
        {
            if (SetProperty(ref _workflowErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasWorkflowError));
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

    public string PartySearchText
    {
        get => _partySearchText;
        set => SetProperty(ref _partySearchText, value);
    }

    public PaymentPartyLookupItemViewModel? SelectedPartyResult
    {
        get => _selectedPartyResult;
        set
        {
            if (SetProperty(ref _selectedPartyResult, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public PaymentPartyLookupItemViewModel? SelectedParty
    {
        get => _selectedParty;
        private set
        {
            if (SetProperty(ref _selectedParty, value))
            {
                OnPropertyChanged(nameof(HasSelectedParty));
                OnPropertyChanged(nameof(SelectedPartyDescription));
                OnPropertyChanged(nameof(CanSearchObligations));
                InvalidatePreparedCreate();
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedParty => SelectedParty is not null;

    public string SelectedPartyDescription => SelectedParty?.DisplayLabel
        ?? $"Aucun {_partyKindLower} sélectionné";

    public string ObligationSearchText
    {
        get => _obligationSearchText;
        set => SetProperty(ref _obligationSearchText, value);
    }

    public bool HasObligations => Obligations.Count > 0;

    public bool ShowNoObligationState => !IsLoadingObligations
        && ((IsCreateVisible && HasSelectedParty) || IsAdvanceVisible)
        && !HasObligations;

    public string PaymentAmount
    {
        get => _paymentAmount;
        set
        {
            if (SetProperty(ref _paymentAmount, value))
            {
                InvalidatePreparedCreate();
                UpdateAllocationSummary();
            }
        }
    }

    public string Observation
    {
        get => _observation;
        set
        {
            if (SetProperty(ref _observation, value))
            {
                InvalidatePreparedCreate();
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
                InvalidatePreparedCreate();
            }
        }
    }

    public string AllocatedSummary
    {
        get => _allocatedSummary;
        private set => SetProperty(ref _allocatedSummary, value);
    }

    public string RemainingSummary
    {
        get => _remainingSummary;
        private set => SetProperty(ref _remainingSummary, value);
    }

    public string RemainingSummaryLabel => IsAdvanceVisible
        ? "Reste d'avance après opération"
        : "Reste non affecté / avance";

    public string DetailReference
    {
        get => _detailReference;
        private set => SetProperty(ref _detailReference, value);
    }

    public string DetailDate
    {
        get => _detailDate;
        private set => SetProperty(ref _detailDate, value);
    }

    public string DetailParty
    {
        get => _detailParty;
        private set => SetProperty(ref _detailParty, value);
    }

    public string DetailAmount
    {
        get => _detailAmount;
        private set => SetProperty(ref _detailAmount, value);
    }

    public string DetailAllocatedAmount
    {
        get => _detailAllocatedAmount;
        private set => SetProperty(ref _detailAllocatedAmount, value);
    }

    public string DetailUnallocatedAmount
    {
        get => _detailUnallocatedAmount;
        private set => SetProperty(ref _detailUnallocatedAmount, value);
    }

    public string DetailPaymentMethod
    {
        get => _detailPaymentMethod;
        private set => SetProperty(ref _detailPaymentMethod, value);
    }

    public string DetailObservation
    {
        get => _detailObservation;
        private set => SetProperty(ref _detailObservation, value);
    }

    public string DetailTargetType
    {
        get => _detailTargetType;
        private set => SetProperty(ref _detailTargetType, value);
    }

    public string DetailPaidAmount
    {
        get => _detailPaidAmount;
        private set => SetProperty(ref _detailPaidAmount, value);
    }

    public string DetailRemainingAmount
    {
        get => _detailRemainingAmount;
        private set => SetProperty(ref _detailRemainingAmount, value);
    }

    public string DetailStatus
    {
        get => _detailStatus;
        private set => SetProperty(ref _detailStatus, value);
    }

    public bool HasDetailHistory => DetailHistory.Count > 0;

    public bool IsTargetObligationLocked
    {
        get => _isTargetObligationLocked;
        private set
        {
            if (SetProperty(ref _isTargetObligationLocked, value))
            {
                OnPropertyChanged(nameof(CanChangeCreateTarget));
                OnPropertyChanged(nameof(CreateInstruction));
            }
        }
    }

    public bool CanChangeCreateTarget => !IsTargetObligationLocked;

    public bool HasDetailAllocations => DetailAllocations.Count > 0;

    public bool CanApplySelectedAdvance => CanApplyAdvancePermission
        && _selectedDetail is { PartyId: not null, UnallocatedAmountMinor: > 0 };

    public string AdvanceAvailable => MoneyFormatter.FormatMinor(_advanceAvailableMinor);

    public string ConfirmationParty
    {
        get => _confirmationParty;
        private set => SetProperty(ref _confirmationParty, value);
    }

    public string ConfirmationAmount
    {
        get => _confirmationAmount;
        private set => SetProperty(ref _confirmationAmount, value);
    }

    public string ConfirmationAllocated
    {
        get => _confirmationAllocated;
        private set => SetProperty(ref _confirmationAllocated, value);
    }

    public string ConfirmationAdvance
    {
        get => _confirmationAdvance;
        private set => SetProperty(ref _confirmationAdvance, value);
    }

    public string ConfirmationPaymentMethod
    {
        get => _confirmationPaymentMethod;
        private set => SetProperty(ref _confirmationPaymentMethod, value);
    }

    public string ConfirmationObligationCount
    {
        get => _confirmationObligationCount;
        private set => SetProperty(ref _confirmationObligationCount, value);
    }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_isStopped || _isActive)
        {
            return _pendingOperation;
        }

        _isActive = true;
        _lifecycleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _pendingOperation = InitializeCoreAsync(_lifecycleCancellation.Token);
        return _pendingOperation;
    }

    public void Deactivate()
    {
        if (!_isActive || _isStopped)
        {
            return;
        }

        _isActive = false;
        Interlocked.Increment(ref _requestVersion);
        _lifecycleCancellation?.Cancel();
        _lifecycleCancellation?.Dispose();
        _lifecycleCancellation = null;
        ClearProtectedState();
    }

    public void Stop()
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;
        _isActive = false;
        Interlocked.Increment(ref _requestVersion);
        _lifecycleCancellation?.Cancel();
        _lifecycleCancellation?.Dispose();
        _lifecycleCancellation = null;
        ClearProtectedState();
    }

    public async Task StopAsync()
    {
        Stop();
        try
        {
            await _pendingOperation;
        }
        catch (OperationCanceledException)
        {
            // Expected during Shell shutdown.
        }
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default) =>
        StartListLoad(PageNumber, cancellationToken);

    public Task ApplyFiltersAsync(CancellationToken cancellationToken = default)
    {
        if (!CanLoadPage)
        {
            return _pendingOperation;
        }

        if (FromDate is DateTime fromDate && ToDate is DateTime toDate && fromDate.Date > toDate.Date)
        {
            ListErrorMessage = InvalidDateRangeMessage;
            return Task.CompletedTask;
        }

        _appliedSearchText = NormalizeOptionalText(SearchText);
        _appliedFromDate = FromDate is DateTime from ? DateOnly.FromDateTime(from.Date) : null;
        _appliedToDate = ToDate is DateTime to ? DateOnly.FromDateTime(to.Date) : null;
        _appliedStatus = SelectedFilterStatus?.Value;
        NotifyFilterStateChanged();
        return StartListLoad(1, cancellationToken);
    }

    public Task ResetFiltersAsync(CancellationToken cancellationToken = default)
    {
        if (!CanLoadPage)
        {
            return _pendingOperation;
        }

        ResetFiltersWithoutLoading();
        return StartListLoad(1, cancellationToken);
    }

    public Task GoToPreviousPageAsync(CancellationToken cancellationToken = default) =>
        PageNumber <= 1 ? Task.CompletedTask : StartListLoad(PageNumber - 1, cancellationToken);

    public Task GoToNextPageAsync(CancellationToken cancellationToken = default) =>
        TotalPages == 0 || PageNumber >= TotalPages
            ? Task.CompletedTask
            : StartListLoad(PageNumber + 1, cancellationToken);

    private bool CanLoadPage => _isActive && !_isStopped && CanViewPayments && !IsBusy && !HasOpenWorkflow;

    private Task Track(Task operation)
    {
        _pendingOperation = operation;
        return operation;
    }

    private bool CanSearchObligations => (IsCreateVisible
            && SelectedParty is not null
            && CanChangeCreateTarget)
        || (IsAdvanceVisible && _selectedDetail?.PartyId is not null);

    private async Task InitializeCoreAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        ListErrorMessage = null;
        var version = Interlocked.Increment(ref _requestVersion);

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var query = CreateQuery(1);
            var payload = await _operationCoordinator.RunAsync(
                async token =>
                {
                    var paymentPermissions = await _authorizationService.GetEffectivePermissionsAsync(
                        _paymentModule,
                        token);
                    var partyPermissions = await _authorizationService.GetEffectivePermissionsAsync(
                        _partyModule,
                        token);
                    var result = paymentPermissions.CanView
                        ? await GetPageAsync(query, token)
                        : null;
                    return new InitializationPayload(paymentPermissions, partyPermissions, result);
                },
                cancellationToken);

            if (!CanApplyResult(identity, version, cancellationToken))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            _permissionsLoaded = true;
            CanViewPayments = payload.PaymentPermissions.CanView;
            CanCreatePayment = payload.PaymentPermissions.CanView
                && payload.PaymentPermissions.CanCreate
                && payload.PartyPermissions.CanView;
            CanApplyAdvancePermission = payload.PaymentPermissions.CanView
                && payload.PaymentPermissions.CanModify;
            OnPropertyChanged(nameof(ShowCreateUnavailable));

            if (!CanViewPayments || payload.Result is null)
            {
                ClearPaymentList();
                ListErrorMessage = $"Vous n'êtes pas autorisé à consulter {_paymentPluralLower}.";
                return;
            }

            ApplyPage(payload.Result);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when navigating away or logging out.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} initialization failed: {1}", PageTitle, exception);
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                ListErrorMessage = $"Impossible de charger {_paymentPluralLower}.";
            }
        }
        finally
        {
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                IsLoading = false;
            }
        }
    }

    private Task StartListLoad(int pageNumber, CancellationToken cancellationToken)
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
        using var operationCancellation = CreateOperationCancellation(cancellationToken);
        var token = operationCancellation.Token;
        IsLoading = true;
        ListErrorMessage = null;
        SuccessMessage = null;
        var version = Interlocked.Increment(ref _requestVersion);

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var result = await _operationCoordinator.RunAsync(
                token => GetPageAsync(CreateQuery(pageNumber), token),
                token);
            if (!CanApplyResult(identity, version, token))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (!result.Success)
            {
                HandleFailure(result.FailureKind, result.FailureMessage, preserveList: true);
                return;
            }

            ApplyPage(result);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected when navigating away or logging out.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} list loading failed: {1}", PageTitle, exception);
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                ListErrorMessage = $"Impossible d'actualiser {_paymentPluralLower}. Les données affichées sont conservées.";
            }
        }
        finally
        {
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                IsLoading = false;
            }
        }
    }

    private async Task OpenDetailAsync(
        PaymentListItemViewModel? obligation,
        CancellationToken cancellationToken = default)
    {
        if (obligation is null || obligation.TargetId <= 0 || !CanLoadPage)
        {
            return;
        }

        IsLoadingDetail = true;
        WorkflowErrorMessage = null;
        using var operationCancellation = CreateOperationCancellation(cancellationToken);
        var token = operationCancellation.Token;
        var version = Interlocked.Increment(ref _requestVersion);

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var result = await _operationCoordinator.RunAsync(
                operationToken => GetObligationDetailAsync(
                    obligation.TargetKind,
                    obligation.TargetId,
                    operationToken),
                token);
            if (!CanApplyResult(identity, version, token))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (!result.Success || result.Item is null)
            {
                HandleFailure(result.FailureKind, result.FailureMessage, preserveList: true);
                return;
            }

            ApplyObligationDetail(result.Item);
            IsDetailVisible = true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected when navigating away or logging out.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} detail loading failed: {1}", PageTitle, exception);
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                WorkflowErrorMessage = "Impossible de charger le détail de l'obligation.";
            }
        }
        finally
        {
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                IsLoadingDetail = false;
            }
        }
    }

    private async Task SearchPartiesAsync(CancellationToken cancellationToken = default)
    {
        if (!IsCreateVisible || !CanCreatePayment || IsBusy)
        {
            return;
        }

        IsLookingUpParties = true;
        WorkflowErrorMessage = null;
        SelectedPartyResult = null;
        using var operationCancellation = CreateOperationCancellation(cancellationToken);
        var token = operationCancellation.Token;
        var version = Interlocked.Increment(ref _requestVersion);

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var result = await _operationCoordinator.RunAsync(
                operationToken => SearchPartiesAsync(
                    NormalizeOptionalText(PartySearchText),
                    PartyLookupLimit,
                    operationToken),
                token);
            if (!CanApplyResult(identity, version, token))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            PartyResults.Clear();
            if (!result.Success)
            {
                HandleFailure(result.FailureKind, result.FailureMessage, preserveList: true);
                return;
            }

            foreach (var item in result.Items)
            {
                PartyResults.Add(new PaymentPartyLookupItemViewModel(item));
            }

            if (PartyResults.Count == 0)
            {
                WorkflowErrorMessage = $"Aucun {_partyKindLower} trouvé.";
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected when navigating away or logging out.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} party lookup failed: {1}", PageTitle, exception);
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                WorkflowErrorMessage = $"Impossible de rechercher les {_partyKindLower}s.";
            }
        }
        finally
        {
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                IsLookingUpParties = false;
            }
        }
    }

    private async Task SelectPartyAsync(CancellationToken cancellationToken = default)
    {
        if (SelectedPartyResult is null || !IsCreateVisible || IsBusy)
        {
            return;
        }

        SelectedParty = SelectedPartyResult;
        SelectedPartyResult = null;
        PartyResults.Clear();
        ObligationSearchText = string.Empty;
        await LoadObligationsCoreAsync(applyAdvance: false, cancellationToken);
    }

    private Task SearchObligationsAsync(CancellationToken cancellationToken = default)
    {
        if (!CanSearchObligations || IsBusy)
        {
            return Task.CompletedTask;
        }

        _pendingOperation = LoadObligationsCoreAsync(IsAdvanceVisible, cancellationToken);
        return _pendingOperation;
    }

    private async Task LoadObligationsCoreAsync(bool applyAdvance, CancellationToken cancellationToken)
    {
        var partyId = applyAdvance ? _selectedDetail?.PartyId : SelectedParty?.Id;
        if (partyId is not > 0)
        {
            return;
        }

        IsLoadingObligations = true;
        WorkflowErrorMessage = null;
        ClearObligations();
        using var operationCancellation = CreateOperationCancellation(cancellationToken);
        var token = operationCancellation.Token;
        var version = Interlocked.Increment(ref _requestVersion);

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var result = await _operationCoordinator.RunAsync(
                operationToken => SearchObligationsAsync(
                    partyId.Value,
                    applyAdvance,
                    NormalizeOptionalText(ObligationSearchText),
                    ObligationLookupLimit,
                    operationToken),
                token);
            if (!CanApplyResult(identity, version, token))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (!result.Success)
            {
                HandleFailure(result.FailureKind, result.FailureMessage, preserveList: true);
                return;
            }

            foreach (var item in result.Items)
            {
                var viewModel = new PaymentObligationItemViewModel(item);
                viewModel.PropertyChanged += ObligationOnPropertyChanged;
                Obligations.Add(viewModel);
            }

            NotifyObligationsChanged();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected when navigating away or logging out.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} obligation lookup failed: {1}", PageTitle, exception);
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                WorkflowErrorMessage = "Impossible de charger les obligations disponibles.";
            }
        }
        finally
        {
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                IsLoadingObligations = false;
            }
        }
    }

    private void PrepareCreate()
    {
        WorkflowErrorMessage = null;
        if (SelectedParty is null)
        {
            WorkflowErrorMessage = $"Sélectionnez un {_partyKindLower}.";
            return;
        }

        if (!MoneyParser.TryParsePositiveMinorUnits(PaymentAmount, out var amountMinor))
        {
            WorkflowErrorMessage = InvalidAmountMessage;
            return;
        }

        if (SelectedPaymentMethod is null)
        {
            WorkflowErrorMessage = "Sélectionnez un mode de paiement.";
            return;
        }

        if (!TryCollectAllocations(out var allocations, out var allocatedMinor))
        {
            return;
        }

        if (allocations.Count == 0)
        {
            WorkflowErrorMessage = "Affectez le paiement à au moins une obligation.";
            return;
        }

        if (allocatedMinor > amountMinor)
        {
            WorkflowErrorMessage = "Le total affecté ne peut pas dépasser le montant du paiement.";
            return;
        }

        _preparedAmountMinor = amountMinor;
        _preparedAllocations = allocations;
        ConfirmationParty = SelectedParty.DisplayName;
        ConfirmationAmount = MoneyFormatter.FormatMinor(amountMinor);
        ConfirmationAllocated = MoneyFormatter.FormatMinor(allocatedMinor);
        ConfirmationAdvance = MoneyFormatter.FormatMinor(amountMinor - allocatedMinor);
        ConfirmationPaymentMethod = SelectedPaymentMethod.DisplayName;
        ConfirmationObligationCount = allocations.Count switch
        {
            1 => "1 obligation affectée",
            _ => $"{allocations.Count} obligations affectées"
        };
        IsCreateConfirmationVisible = true;
    }

    private async Task ConfirmCreateAsync(CancellationToken cancellationToken = default)
    {
        if (!IsCreateConfirmationVisible
            || SelectedParty is null
            || SelectedPaymentMethod is null
            || _preparedAllocations.Count == 0
            || !CanCreatePayment)
        {
            return;
        }

        IsSaving = true;
        WorkflowErrorMessage = null;
        SuccessMessage = null;
        using var operationCancellation = CreateOperationCancellation(cancellationToken);
        var token = operationCancellation.Token;
        var version = Interlocked.Increment(ref _requestVersion);
        var partyId = SelectedParty.Id;
        var paymentMethod = SelectedPaymentMethod.Value;
        var observation = NormalizeOptionalText(Observation);
        var detailTarget = _preparedAllocations.Count == 1
            ? _preparedAllocations[0]
            : null;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var result = await _operationCoordinator.RunAsync(
                operationToken => CreatePaymentAsync(
                    partyId,
                    _preparedAmountMinor,
                    paymentMethod,
                    observation,
                    _preparedAllocations,
                    operationToken),
                token);
            if (!CanApplyResult(identity, version, token))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (!result.Success)
            {
                IsCreateConfirmationVisible = false;
                HandleFailure(result.FailureKind, result.FailureMessage, preserveList: true);
                return;
            }

            ResetCreateForm();
            IsCreateVisible = false;
            SuccessMessage = $"{_partyKindCapitalized} : paiement enregistré.";

            var pageResult = await _operationCoordinator.RunAsync(
                operationToken => GetPageAsync(CreateQuery(1), operationToken),
                token);
            if (!CanApplyResult(identity, version, token))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (pageResult.Success)
            {
                ApplyPage(pageResult);
            }
            else
            {
                ListErrorMessage = "Le paiement est enregistré, mais la liste n'a pas pu être actualisée.";
            }

            if (detailTarget is not null)
            {
                var detailResult = await _operationCoordinator.RunAsync(
                    operationToken => GetObligationDetailAsync(
                        detailTarget.TargetKind,
                        detailTarget.TargetId,
                        operationToken),
                    token);
                if (CanApplyResult(identity, version, token)
                    && detailResult.Success
                    && detailResult.Item is not null)
                {
                    ApplyObligationDetail(detailResult.Item);
                    IsDetailVisible = true;
                }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected when navigating away or logging out.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} creation failed: {1}", PageTitle, exception);
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                WorkflowErrorMessage = "Impossible d'enregistrer le paiement.";
            }
        }
        finally
        {
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                IsSaving = false;
            }
        }
    }

    private async Task ShowAdvanceAsync(CancellationToken cancellationToken = default)
    {
        if (!CanApplySelectedAdvance || _selectedDetail is null || IsBusy)
        {
            return;
        }

        _advanceAvailableMinor = _selectedDetail.UnallocatedAmountMinor;
        OnPropertyChanged(nameof(AdvanceAvailable));
        OnPropertyChanged(nameof(RemainingSummaryLabel));
        DetailParty = _selectedDetail.PartyName ?? string.Empty;
        IsDetailVisible = false;
        IsAdvanceVisible = true;
        ObligationSearchText = string.Empty;
        UpdateAllocationSummary();
        await LoadObligationsCoreAsync(applyAdvance: true, cancellationToken);
    }

    private async Task ApplyAdvanceAsync(CancellationToken cancellationToken = default)
    {
        if (!IsAdvanceVisible || !CanApplySelectedAdvance || _selectedDetail is null)
        {
            return;
        }

        WorkflowErrorMessage = null;
        if (!TryCollectAllocations(out var allocations, out var allocatedMinor))
        {
            return;
        }

        if (allocations.Count == 0)
        {
            WorkflowErrorMessage = "Affectez un montant à au moins une obligation.";
            return;
        }

        if (allocatedMinor > _advanceAvailableMinor)
        {
            WorkflowErrorMessage = "Le montant affecté dépasse l'avance disponible.";
            return;
        }

        IsApplyingAdvance = true;
        SuccessMessage = null;
        using var operationCancellation = CreateOperationCancellation(cancellationToken);
        var token = operationCancellation.Token;
        var version = Interlocked.Increment(ref _requestVersion);
        var paymentId = _selectedDetail.Id;

        try
        {
            if (!TryCaptureAuthenticatedSession(out var identity))
            {
                ClearProtectedState("Votre session n'est plus active.");
                return;
            }

            var result = await _operationCoordinator.RunAsync(
                operationToken => ApplyAdvanceAsync(paymentId, allocations, operationToken),
                token);
            if (!CanApplyResult(identity, version, token))
            {
                FailClosedIfSessionChanged(identity);
                return;
            }

            if (!result.Success)
            {
                HandleFailure(result.FailureKind, result.FailureMessage, preserveList: true);
                return;
            }

            IsAdvanceVisible = false;
            ClearObligations();
            SuccessMessage = "Avance affectée.";

            var pageResult = await _operationCoordinator.RunAsync(
                operationToken => GetPageAsync(CreateQuery(PageNumber), operationToken),
                token);
            if (CanApplyResult(identity, version, token) && pageResult.Success)
            {
                ApplyPage(pageResult);
            }

            var detailResult = await _operationCoordinator.RunAsync(
                operationToken => GetDetailAsync(paymentId, operationToken),
                token);
            if (CanApplyResult(identity, version, token)
                && detailResult.Success
                && detailResult.Item is not null)
            {
                ApplyDetail(detailResult.Item);
                IsDetailVisible = true;
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Expected when navigating away or logging out.
        }
        catch (Exception exception)
        {
            Trace.TraceError("{0} advance application failed: {1}", PageTitle, exception);
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                WorkflowErrorMessage = "Impossible d'affecter l'avance.";
            }
        }
        finally
        {
            if (IsCurrentView && version == Volatile.Read(ref _requestVersion))
            {
                IsApplyingAdvance = false;
            }
        }
    }

    private bool TryCollectAllocations(
        out IReadOnlyList<PaymentAllocationRequest> allocations,
        out long totalMinor)
    {
        var items = new List<PaymentAllocationRequest>();
        totalMinor = 0;
        foreach (var obligation in Obligations)
        {
            if (!obligation.TryGetAllocation(out var allocation))
            {
                WorkflowErrorMessage =
                    $"Le montant affecté à {obligation.Reference} doit être positif avec au maximum deux décimales.";
                allocations = [];
                return false;
            }

            if (allocation is null)
            {
                continue;
            }

            if (allocation.AmountMinor > obligation.OutstandingAmountMinor)
            {
                WorkflowErrorMessage =
                    $"Le montant affecté à {obligation.Reference} dépasse le reste à payer.";
                allocations = [];
                return false;
            }

            try
            {
                totalMinor = checked(totalMinor + allocation.AmountMinor);
            }
            catch (OverflowException)
            {
                WorkflowErrorMessage = "Le total des affectations est trop élevé.";
                allocations = [];
                return false;
            }

            items.Add(allocation);
        }

        allocations = items;
        return true;
    }

    private void ApplyPage(PaymentPageData result)
    {
        Payments.Clear();
        foreach (var item in result.Items)
        {
            Payments.Add(new PaymentListItemViewModel(item));
        }

        PageNumber = result.PageNumber;
        TotalPages = result.TotalPages;
        TotalCount = result.TotalCount;
        HasLoadedList = true;
        ListErrorMessage = null;
        NotifyPaymentsChanged();
    }

    private void ApplyDetail(PaymentDetailData item)
    {
        _selectedDetail = item;
        DetailReference = item.Reference;
        DetailDate = item.PaymentDate.ToString("dd/MM/yyyy");
        DetailParty = string.IsNullOrWhiteSpace(item.PartyName) ? "Client anonyme" : item.PartyName;
        DetailAmount = MoneyFormatter.FormatMinor(item.AmountMinor);
        DetailAllocatedAmount = MoneyFormatter.FormatMinor(item.AllocatedAmountMinor);
        DetailUnallocatedAmount = MoneyFormatter.FormatMinor(item.UnallocatedAmountMinor);
        DetailPaymentMethod = PaymentLabels.FormatPaymentMethod(item.PaymentMethod);
        DetailObservation = string.IsNullOrWhiteSpace(item.Observation) ? "—" : item.Observation;
        DetailAllocations.Clear();
        foreach (var allocation in item.Allocations)
        {
            DetailAllocations.Add(new PaymentAllocationDetailItemViewModel(allocation));
        }

        OnPropertyChanged(nameof(HasDetailAllocations));
        OnPropertyChanged(nameof(CanApplySelectedAdvance));
        RaiseCommandCanExecuteChanged();
    }

    private void ApplyObligationDetail(PaymentObligationDetailData item)
    {
        _selectedObligationDetail = item;
        _selectedDetail = null;
        DetailTargetType = PaymentLabels.FormatTargetType(item.TargetKind);
        DetailReference = item.Reference;
        DetailDate = item.BusinessDate.ToString("dd/MM/yyyy");
        DetailParty = string.IsNullOrWhiteSpace(item.PartyName) ? "Client anonyme" : item.PartyName;
        DetailAmount = MoneyFormatter.FormatMinor(item.OriginalAmountMinor);
        DetailPaidAmount = MoneyFormatter.FormatMinor(item.PaidAmountMinor);
        DetailRemainingAmount = MoneyFormatter.FormatMinor(item.RemainingAmountMinor);
        DetailStatus = PaymentLabels.FormatStatus(item.Status);
        DetailHistory.Clear();
        foreach (var historyItem in item.History)
        {
            DetailHistory.Add(new PaymentHistoryItemViewModel(historyItem));
        }

        OnPropertyChanged(nameof(HasDetailHistory));
        OnPropertyChanged(nameof(CanApplySelectedAdvance));
        RaiseCommandCanExecuteChanged();
    }

    private void HandleFailure(
        PaymentFailureKind failureKind,
        string? message,
        bool preserveList)
    {
        if (failureKind is PaymentFailureKind.NotAuthenticated or PaymentFailureKind.Forbidden)
        {
            ClearProtectedState(message ?? "Votre session n'est plus active.");
            CanViewPayments = false;
            CanCreatePayment = false;
            CanApplyAdvancePermission = false;
            return;
        }

        var effectiveMessage = message ?? "L'opération n'a pas pu être terminée.";
        if (HasOpenWorkflow)
        {
            WorkflowErrorMessage = effectiveMessage;
        }
        else
        {
            ListErrorMessage = effectiveMessage;
        }

        if (!preserveList && failureKind is PaymentFailureKind.NotFound)
        {
            ClearPaymentList();
        }
    }

    private void ShowCreate()
    {
        ResetCreateForm();
        SuccessMessage = null;
        IsCreateVisible = true;
    }

    private void ShowCreateForObligation(PaymentListItemViewModel? obligation)
    {
        if (obligation is not { HasRemaining: true, PartyId: > 0 }
            || !CanCreatePayment
            || IsBusy
            || HasOpenWorkflow)
        {
            return;
        }

        ResetCreateForm();
        IsTargetObligationLocked = true;
        SuccessMessage = null;
        SelectedParty = new PaymentPartyLookupItemViewModel(
            new PartyLookupData(obligation.PartyId.Value, obligation.PartyName, null));

        var target = new PaymentObligationItemViewModel(new PaymentObligationData(
            obligation.TargetKind,
            obligation.TargetId,
            obligation.Reference,
            obligation.BusinessDateValue,
            null,
            obligation.OriginalAmountMinor,
            obligation.RemainingAmountMinor))
        {
            AllocationAmount = MoneyFormatter.FormatMinor(obligation.RemainingAmountMinor)
        };
        target.PropertyChanged += ObligationOnPropertyChanged;
        Obligations.Add(target);
        PaymentAmount = MoneyFormatter.FormatMinor(obligation.RemainingAmountMinor);
        NotifyObligationsChanged();
        UpdateAllocationSummary();
        IsCreateVisible = true;
    }

    private void CancelCreate()
    {
        ResetCreateForm();
        IsCreateVisible = false;
    }

    private void CloseDetail()
    {
        IsDetailVisible = false;
        ClearDetail();
        WorkflowErrorMessage = null;
    }

    private void CancelAdvance()
    {
        IsAdvanceVisible = false;
        ClearObligations();
        WorkflowErrorMessage = null;
        OnPropertyChanged(nameof(RemainingSummaryLabel));
        if (_selectedDetail is not null)
        {
            IsDetailVisible = true;
        }
    }

    private void ClearSelectedParty()
    {
        SelectedParty = null;
        SelectedPartyResult = null;
        PartyResults.Clear();
        ClearObligations();
        WorkflowErrorMessage = null;
    }

    private void ResetCreateForm()
    {
        IsTargetObligationLocked = false;
        IsCreateConfirmationVisible = false;
        PartySearchText = string.Empty;
        SelectedPartyResult = null;
        SelectedParty = null;
        PartyResults.Clear();
        ObligationSearchText = string.Empty;
        ClearObligations();
        PaymentAmount = string.Empty;
        Observation = string.Empty;
        SelectedPaymentMethod = PaymentMethods[0];
        WorkflowErrorMessage = null;
        _preparedAmountMinor = 0;
        _preparedAllocations = [];
        UpdateAllocationSummary();
    }

    private void ResetFiltersWithoutLoading()
    {
        SearchText = string.Empty;
        FromDate = null;
        ToDate = null;
        SelectedFilterStatus = FilterStatuses[0];
        _appliedSearchText = null;
        _appliedFromDate = null;
        _appliedToDate = null;
        _appliedStatus = null;
        NotifyFilterStateChanged();
    }

    private void ClearProtectedState(string? message = null)
    {
        ClearPaymentList();
        ClearObligations();
        PartyResults.Clear();
        ClearDetail();
        IsCreateVisible = false;
        IsCreateConfirmationVisible = false;
        IsDetailVisible = false;
        IsAdvanceVisible = false;
        IsLoading = false;
        IsLoadingDetail = false;
        IsLookingUpParties = false;
        IsLoadingObligations = false;
        IsSaving = false;
        IsApplyingAdvance = false;
        _permissionsLoaded = false;
        CanViewPayments = false;
        CanCreatePayment = false;
        CanApplyAdvancePermission = false;
        SuccessMessage = null;
        WorkflowErrorMessage = null;
        ListErrorMessage = message;
        RaiseCommandCanExecuteChanged();
    }

    private void ClearPaymentList()
    {
        Payments.Clear();
        PageNumber = 1;
        TotalPages = 0;
        TotalCount = 0;
        HasLoadedList = false;
        NotifyPaymentsChanged();
    }

    private void ClearDetail()
    {
        _selectedDetail = null;
        _selectedObligationDetail = null;
        DetailReference = string.Empty;
        DetailDate = string.Empty;
        DetailParty = string.Empty;
        DetailAmount = string.Empty;
        DetailAllocatedAmount = string.Empty;
        DetailUnallocatedAmount = string.Empty;
        DetailPaymentMethod = string.Empty;
        DetailObservation = string.Empty;
        DetailTargetType = string.Empty;
        DetailPaidAmount = string.Empty;
        DetailRemainingAmount = string.Empty;
        DetailStatus = string.Empty;
        DetailAllocations.Clear();
        DetailHistory.Clear();
        OnPropertyChanged(nameof(HasDetailAllocations));
        OnPropertyChanged(nameof(HasDetailHistory));
        OnPropertyChanged(nameof(CanApplySelectedAdvance));
        RaiseCommandCanExecuteChanged();
    }

    private void ClearObligations()
    {
        foreach (var item in Obligations)
        {
            item.PropertyChanged -= ObligationOnPropertyChanged;
        }

        Obligations.Clear();
        NotifyObligationsChanged();
        UpdateAllocationSummary();
    }

    private void ObligationOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PaymentObligationItemViewModel.AllocationAmount))
        {
            InvalidatePreparedCreate();
            UpdateAllocationSummary();
        }
    }

    private void UpdateAllocationSummary()
    {
        long totalMinor = 0;
        var valid = true;
        foreach (var obligation in Obligations)
        {
            if (!obligation.TryGetAllocation(out var allocation))
            {
                valid = false;
                continue;
            }

            if (allocation is null)
            {
                continue;
            }

            try
            {
                totalMinor = checked(totalMinor + allocation.AmountMinor);
            }
            catch (OverflowException)
            {
                valid = false;
                break;
            }
        }

        AllocatedSummary = valid ? MoneyFormatter.FormatMinor(totalMinor) : "Montant invalide";
        if (IsAdvanceVisible)
        {
            RemainingSummary = valid && totalMinor <= _advanceAvailableMinor
                ? MoneyFormatter.FormatMinor(_advanceAvailableMinor - totalMinor)
                : "Montant invalide";
            return;
        }

        RemainingSummary = valid
            && MoneyParser.TryParsePositiveMinorUnits(PaymentAmount, out var amountMinor)
            && totalMinor <= amountMinor
                ? MoneyFormatter.FormatMinor(amountMinor - totalMinor)
                : MoneyFormatter.FormatMinor(0);
    }

    private void InvalidatePreparedCreate()
    {
        IsCreateConfirmationVisible = false;
        _preparedAmountMinor = 0;
        _preparedAllocations = [];
    }

    private PaymentListQueryData CreateQuery(int pageNumber) => new(
        pageNumber,
        PageSize,
        _appliedFromDate,
        _appliedToDate,
        _appliedSearchText,
        _appliedStatus);

    private CancellationTokenSource CreateOperationCancellation(CancellationToken cancellationToken)
    {
        if (_lifecycleCancellation is null)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        }

        return CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifecycleCancellation.Token);
    }

    private bool IsCurrentView => _isActive && !_isStopped;

    private bool CanApplyResult(
        SessionIdentity identity,
        long version,
        CancellationToken cancellationToken) =>
        IsCurrentView
        && !cancellationToken.IsCancellationRequested
        && version == Volatile.Read(ref _requestVersion)
        && IsSameAuthenticatedSession(identity);

    private void FailClosedIfSessionChanged(SessionIdentity identity)
    {
        if (!IsSameAuthenticatedSession(identity))
        {
            ClearProtectedState("Votre session n'est plus active.");
        }
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

    private void SetBusyProperty(
        ref bool field,
        bool value,
        [CallerMemberName] string? propertyName = null)
    {
        if (SetProperty(ref field, value, propertyName))
        {
            OnPropertyChanged(nameof(IsBusy));
            OnPropertyChanged(nameof(IsMainEnabled));
            OnPropertyChanged(nameof(ShowEmptyState));
            RaiseCommandCanExecuteChanged();
        }
    }

    private void NotifyPaginationChanged()
    {
        OnPropertyChanged(nameof(PageNumberDescription));
        RaiseCommandCanExecuteChanged();
    }

    private void NotifyPaymentsChanged()
    {
        OnPropertyChanged(nameof(HasPayments));
        OnPropertyChanged(nameof(ShowEmptyState));
    }

    private void NotifyObligationsChanged()
    {
        OnPropertyChanged(nameof(HasObligations));
        OnPropertyChanged(nameof(ShowNoObligationState));
    }

    private void NotifyFilterStateChanged()
    {
        OnPropertyChanged(nameof(HasActiveFilters));
        OnPropertyChanged(nameof(EmptyStateMessage));
    }

    private void ClearDateValidationMessage()
    {
        if (ListErrorMessage == InvalidDateRangeMessage)
        {
            ListErrorMessage = null;
        }
    }

    private void RaiseCommandCanExecuteChanged()
    {
        _refreshRelayCommand?.RaiseCanExecuteChanged();
        _applyFiltersRelayCommand?.RaiseCanExecuteChanged();
        _resetFiltersRelayCommand?.RaiseCanExecuteChanged();
        _previousPageRelayCommand?.RaiseCanExecuteChanged();
        _nextPageRelayCommand?.RaiseCanExecuteChanged();
        _openDetailRelayCommand?.RaiseCanExecuteChanged();
        _showCreateForObligationRelayCommand?.RaiseCanExecuteChanged();
        _closeDetailRelayCommand?.RaiseCanExecuteChanged();
        _showCreateRelayCommand?.RaiseCanExecuteChanged();
        _cancelCreateRelayCommand?.RaiseCanExecuteChanged();
        _searchPartiesRelayCommand?.RaiseCanExecuteChanged();
        _selectPartyRelayCommand?.RaiseCanExecuteChanged();
        _clearSelectedPartyRelayCommand?.RaiseCanExecuteChanged();
        _searchObligationsRelayCommand?.RaiseCanExecuteChanged();
        _prepareCreateRelayCommand?.RaiseCanExecuteChanged();
        _cancelCreateConfirmationRelayCommand?.RaiseCanExecuteChanged();
        _confirmCreateRelayCommand?.RaiseCanExecuteChanged();
        _showAdvanceRelayCommand?.RaiseCanExecuteChanged();
        _cancelAdvanceRelayCommand?.RaiseCanExecuteChanged();
        _applyAdvanceRelayCommand?.RaiseCanExecuteChanged();
    }

    private static string? NormalizeOptionalText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal abstract Task<PaymentPageData> GetPageAsync(
        PaymentListQueryData query,
        CancellationToken cancellationToken);

    internal abstract Task<PaymentDetailResponse> GetDetailAsync(
        long paymentId,
        CancellationToken cancellationToken);

    internal abstract Task<PaymentObligationDetailResponse> GetObligationDetailAsync(
        PaymentTargetKind targetKind,
        long targetId,
        CancellationToken cancellationToken);

    internal abstract Task<PartyLookupResponse> SearchPartiesAsync(
        string? searchText,
        int limit,
        CancellationToken cancellationToken);

    internal abstract Task<PaymentObligationResponse> SearchObligationsAsync(
        long partyId,
        bool applyAdvance,
        string? searchText,
        int limit,
        CancellationToken cancellationToken);

    internal abstract Task<PaymentCreateResponse> CreatePaymentAsync(
        long partyId,
        long amountMinor,
        PaymentMethod paymentMethod,
        string? observation,
        IReadOnlyList<PaymentAllocationRequest> allocations,
        CancellationToken cancellationToken);

    internal abstract Task<AdvanceApplyResponse> ApplyAdvanceAsync(
        long paymentId,
        IReadOnlyList<PaymentAllocationRequest> allocations,
        CancellationToken cancellationToken);

    private sealed record InitializationPayload(
        EffectiveModulePermission PaymentPermissions,
        EffectiveModulePermission PartyPermissions,
        PaymentPageData? Result);

    private readonly record struct SessionIdentity(
        long UserId,
        string? Username,
        UserRole Role);
}

internal sealed record PaymentListQueryData(
    int PageNumber,
    int PageSize,
    DateOnly? FromDate,
    DateOnly? ToDate,
    string? SearchText,
    PaymentObligationStatus? Status);
