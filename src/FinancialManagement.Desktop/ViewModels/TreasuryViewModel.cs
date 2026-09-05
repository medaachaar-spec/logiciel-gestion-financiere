using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class TreasuryViewModel : ViewModelBase
{
    private const string SummaryError = "Impossible de charger la situation de trésorerie.";
    private const string OperationError = "Impossible d'effectuer l'opération.";
    private const string RefreshAfterWriteError =
        "L'opération a été enregistrée, mais l'actualisation a échoué.";
    private const string SignedAmountError =
        "Saisissez un montant valide avec au maximum deux décimales.";
    private const string PositiveAmountError =
        "Saisissez un montant strictement positif avec au maximum deux décimales.";

    private readonly ITreasuryService _treasuryService;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthorizationService _authorizationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly TimeProvider _timeProvider;
    private readonly AsyncRelayCommand _refreshRelayCommand;
    private readonly AsyncRelayCommand _createOpeningBalanceRelayCommand;
    private readonly AsyncRelayCommand _createTransferRelayCommand;
    private readonly RelayCommand _showOpeningFormRelayCommand;
    private readonly RelayCommand _cancelOpeningFormRelayCommand;
    private readonly RelayCommand _showTransferFormRelayCommand;
    private readonly RelayCommand _cancelTransferFormRelayCommand;
    private bool _isLoading;
    private bool _isSaving;
    private bool _hasData;
    private bool _isInitialized;
    private string _cashBalance = string.Empty;
    private string _bankBalance = string.Empty;
    private string _totalTreasury = string.Empty;
    private bool _cashBalanceIsNegative;
    private bool _bankBalanceIsNegative;
    private bool _totalTreasuryIsNegative;
    private string _openingDescription = string.Empty;
    private string? _summaryErrorMessage;
    private string? _operationErrorMessage;
    private string? _successMessage;
    private bool _isOpeningFormVisible;
    private bool _isTransferFormVisible;
    private DateTime? _openingDate;
    private string _openingCashAmount = string.Empty;
    private string _openingBankAmount = string.Empty;
    private TreasuryAccountOptionViewModel? _selectedFromAccount;
    private TreasuryAccountOptionViewModel? _selectedToAccount;
    private string _transferAmount = string.Empty;
    private string _transferObservation = string.Empty;
    private bool _initialLoadRequested;
    private bool _isStopped;
    private CancellationTokenSource? _operationCancellation;
    private Task _pendingOperation = Task.CompletedTask;

    public TreasuryViewModel(
        ITreasuryService treasuryService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(treasuryService);
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authorizationService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _treasuryService = treasuryService;
        _currentUserSession = currentUserSession;
        _authorizationService = authorizationService;
        _operationCoordinator = operationCoordinator;
        _timeProvider = timeProvider;

        AccountOptions =
        [
            new TreasuryAccountOptionViewModel(TreasuryAccount.Cash, "Caisse"),
            new TreasuryAccountOptionViewModel(TreasuryAccount.Bank, "Banque")
        ];
        ResetOpeningForm();
        ResetTransferForm();

        _refreshRelayCommand = new AsyncRelayCommand(
            _ => RefreshAsync(),
            _ => !IsBusy && !_isStopped);
        _createOpeningBalanceRelayCommand = new AsyncRelayCommand(
            _ => CreateOpeningBalanceAsync(),
            _ => !IsBusy && IsOpeningFormVisible && ShowUninitializedState && !_isStopped);
        _createTransferRelayCommand = new AsyncRelayCommand(
            _ => CreateTransferAsync(),
            _ => !IsBusy && IsTransferFormVisible && ShowInitializedState && !_isStopped);
        _showOpeningFormRelayCommand = new RelayCommand(
            _ => ShowOpeningForm(),
            _ => !IsBusy && ShowUninitializedState && !IsOpeningFormVisible && !_isStopped);
        _cancelOpeningFormRelayCommand = new RelayCommand(
            _ => CancelOpeningForm(),
            _ => !IsBusy && IsOpeningFormVisible && !_isStopped);
        _showTransferFormRelayCommand = new RelayCommand(
            _ => ShowTransferForm(),
            _ => !IsBusy && ShowInitializedState && !IsTransferFormVisible && !_isStopped);
        _cancelTransferFormRelayCommand = new RelayCommand(
            _ => CancelTransferForm(),
            _ => !IsBusy && IsTransferFormVisible && !_isStopped);

        RefreshCommand = _refreshRelayCommand;
        CreateOpeningBalanceCommand = _createOpeningBalanceRelayCommand;
        CreateTransferCommand = _createTransferRelayCommand;
        ShowOpeningFormCommand = _showOpeningFormRelayCommand;
        CancelOpeningFormCommand = _cancelOpeningFormRelayCommand;
        ShowTransferFormCommand = _showTransferFormRelayCommand;
        CancelTransferFormCommand = _cancelTransferFormRelayCommand;
    }

    public IReadOnlyList<TreasuryAccountOptionViewModel> AccountOptions { get; }

    public ICommand RefreshCommand { get; }

    public ICommand CreateOpeningBalanceCommand { get; }

    public ICommand CreateTransferCommand { get; }

    public ICommand ShowOpeningFormCommand { get; }

    public ICommand CancelOpeningFormCommand { get; }

    public ICommand ShowTransferFormCommand { get; }

    public ICommand CancelTransferFormCommand { get; }

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

    public bool IsBusy => IsLoading || IsSaving;

    public bool HasData
    {
        get => _hasData;
        private set
        {
            if (SetProperty(ref _hasData, value))
            {
                NotifySummaryStateChanged();
            }
        }
    }

    public bool IsInitialized
    {
        get => _isInitialized;
        private set
        {
            if (SetProperty(ref _isInitialized, value))
            {
                NotifySummaryStateChanged();
            }
        }
    }

    public bool ShowInitializedState => HasData && IsInitialized;

    public bool ShowUninitializedState => HasData && !IsInitialized;

    public string CashBalance
    {
        get => _cashBalance;
        private set => SetProperty(ref _cashBalance, value);
    }

    public string BankBalance
    {
        get => _bankBalance;
        private set => SetProperty(ref _bankBalance, value);
    }

    public string TotalTreasury
    {
        get => _totalTreasury;
        private set => SetProperty(ref _totalTreasury, value);
    }

    public bool CashBalanceIsNegative
    {
        get => _cashBalanceIsNegative;
        private set => SetProperty(ref _cashBalanceIsNegative, value);
    }

    public bool BankBalanceIsNegative
    {
        get => _bankBalanceIsNegative;
        private set => SetProperty(ref _bankBalanceIsNegative, value);
    }

    public bool TotalTreasuryIsNegative
    {
        get => _totalTreasuryIsNegative;
        private set => SetProperty(ref _totalTreasuryIsNegative, value);
    }

    public string OpeningDescription
    {
        get => _openingDescription;
        private set => SetProperty(ref _openingDescription, value);
    }

    public bool HasSummaryError => !string.IsNullOrWhiteSpace(SummaryErrorMessage);

    public string? SummaryErrorMessage
    {
        get => _summaryErrorMessage;
        private set
        {
            if (SetProperty(ref _summaryErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasSummaryError));
            }
        }
    }

    public bool HasOperationError => !string.IsNullOrWhiteSpace(OperationErrorMessage);

    public string? OperationErrorMessage
    {
        get => _operationErrorMessage;
        private set
        {
            if (SetProperty(ref _operationErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasOperationError));
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

    public bool IsOpeningFormVisible
    {
        get => _isOpeningFormVisible;
        private set
        {
            if (SetProperty(ref _isOpeningFormVisible, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool IsTransferFormVisible
    {
        get => _isTransferFormVisible;
        private set
        {
            if (SetProperty(ref _isTransferFormVisible, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public DateTime? OpeningDate
    {
        get => _openingDate;
        set => SetProperty(ref _openingDate, value);
    }

    public string OpeningCashAmount
    {
        get => _openingCashAmount;
        set => SetProperty(ref _openingCashAmount, value);
    }

    public string OpeningBankAmount
    {
        get => _openingBankAmount;
        set => SetProperty(ref _openingBankAmount, value);
    }

    public TreasuryAccountOptionViewModel? SelectedFromAccount
    {
        get => _selectedFromAccount;
        set => SetProperty(ref _selectedFromAccount, value);
    }

    public TreasuryAccountOptionViewModel? SelectedToAccount
    {
        get => _selectedToAccount;
        set => SetProperty(ref _selectedToAccount, value);
    }

    public string TransferAmount
    {
        get => _transferAmount;
        set => SetProperty(ref _transferAmount, value);
    }

    public string TransferObservation
    {
        get => _transferObservation;
        set => SetProperty(ref _transferObservation, value);
    }

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        if (_initialLoadRequested || _isStopped)
        {
            return _pendingOperation;
        }

        _initialLoadRequested = true;
        return StartSummaryLoad(cancellationToken);
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        if (_isStopped || IsBusy)
        {
            return _pendingOperation;
        }

        _initialLoadRequested = true;
        return StartSummaryLoad(cancellationToken);
    }

    public Task CreateOpeningBalanceAsync(CancellationToken cancellationToken = default)
    {
        if (_isStopped || IsBusy)
        {
            return _pendingOperation;
        }

        _pendingOperation = CreateOpeningBalanceCoreAsync(cancellationToken);
        return _pendingOperation;
    }

    public Task CreateTransferAsync(CancellationToken cancellationToken = default)
    {
        if (_isStopped || IsBusy)
        {
            return _pendingOperation;
        }

        _pendingOperation = CreateTransferCoreAsync(cancellationToken);
        return _pendingOperation;
    }

    public void Stop()
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;
        _operationCancellation?.Cancel();
        ClearSummaryData();
        IsOpeningFormVisible = false;
        IsTransferFormVisible = false;
        SummaryErrorMessage = null;
        OperationErrorMessage = null;
        SuccessMessage = null;
        IsLoading = false;
        IsSaving = false;
        RaiseCommandCanExecuteChanged();
    }

    public Task StopAsync()
    {
        Stop();
        return _pendingOperation;
    }

    private Task StartSummaryLoad(CancellationToken cancellationToken)
    {
        OperationErrorMessage = null;
        SuccessMessage = null;
        _pendingOperation = LoadSummaryCoreAsync(cancellationToken);
        return _pendingOperation;
    }

    private async Task LoadSummaryCoreAsync(CancellationToken cancellationToken)
    {
        IsLoading = true;
        SummaryErrorMessage = null;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!CanAccessTreasury())
            {
                ClearSummaryData();
                SummaryErrorMessage = SummaryError;
                return;
            }

            var userId = _currentUserSession.UserId;
            var result = await GetSummaryAsync(cancellation.Token);
            if (!CanApplyResult(userId, cancellation))
            {
                return;
            }

            if (!result.Success)
            {
                ClearSummaryData();
                SummaryErrorMessage = SummaryError;
                return;
            }

            ApplySummary(result);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the owning Shell is being closed.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Treasury summary loading failed: {0}", exception);
            if (!_isStopped)
            {
                SummaryErrorMessage = SummaryError;
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

    private async Task CreateOpeningBalanceCoreAsync(CancellationToken cancellationToken)
    {
        OperationErrorMessage = null;
        SuccessMessage = null;

        if (OpeningDate is not DateTime openingDate)
        {
            OperationErrorMessage = "Sélectionnez une date d'ouverture.";
            return;
        }

        if (!MoneyParser.TryParseSignedMinorUnits(OpeningCashAmount, out var cashAmountMinor)
            || !MoneyParser.TryParseSignedMinorUnits(OpeningBankAmount, out var bankAmountMinor))
        {
            OperationErrorMessage = SignedAmountError;
            return;
        }

        IsSaving = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!CanAccessTreasury())
            {
                ClearSummaryData();
                OperationErrorMessage = OperationError;
                return;
            }

            var userId = _currentUserSession.UserId;
            var command = new CreateTreasuryOpeningBalanceCommand(
                DateOnly.FromDateTime(openingDate.Date),
                cashAmountMinor,
                bankAmountMinor);
            var result = await _operationCoordinator.RunAsync(
                token => _treasuryService.CreateOpeningBalanceAsync(command, token),
                cancellation.Token);
            if (!CanApplyResult(userId, cancellation))
            {
                return;
            }

            if (!result.Success)
            {
                if (result.FailureReason is CreateTreasuryOpeningBalanceFailureReason.NotAuthenticated
                    or CreateTreasuryOpeningBalanceFailureReason.Forbidden)
                {
                    ClearSummaryData();
                }

                OperationErrorMessage = result.FailureReason switch
                {
                    CreateTreasuryOpeningBalanceFailureReason.AlreadyInitialized =>
                        "La trésorerie est déjà initialisée.",
                    _ => OperationError
                };
                return;
            }

            ResetOpeningForm();
            IsOpeningFormVisible = false;
            if (await RefreshAfterWriteAsync(userId, cancellation.Token))
            {
                SuccessMessage = "Trésorerie initialisée avec succès.";
            }
            else if (!_isStopped)
            {
                OperationErrorMessage = RefreshAfterWriteError;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the owning Shell is being closed.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Treasury opening creation failed: {0}", exception);
            if (!_isStopped)
            {
                OperationErrorMessage = OperationError;
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

    private async Task CreateTransferCoreAsync(CancellationToken cancellationToken)
    {
        OperationErrorMessage = null;
        SuccessMessage = null;

        if (SelectedFromAccount is not { } fromAccount
            || SelectedToAccount is not { } toAccount)
        {
            OperationErrorMessage = "Sélectionnez les comptes source et destination.";
            return;
        }

        if (fromAccount.Account == toAccount.Account)
        {
            OperationErrorMessage = "Les comptes source et destination doivent être différents.";
            return;
        }

        if (!MoneyParser.TryParsePositiveMinorUnits(TransferAmount, out var amountMinor))
        {
            OperationErrorMessage = PositiveAmountError;
            return;
        }

        IsSaving = true;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _operationCancellation = cancellation;

        try
        {
            if (!CanAccessTreasury())
            {
                ClearSummaryData();
                OperationErrorMessage = OperationError;
                return;
            }

            var userId = _currentUserSession.UserId;
            var observation = string.IsNullOrWhiteSpace(TransferObservation)
                ? null
                : TransferObservation.Trim();
            var command = new CreateTreasuryTransferCommand(
                fromAccount.Account,
                toAccount.Account,
                amountMinor,
                observation);
            var result = await _operationCoordinator.RunAsync(
                token => _treasuryService.CreateTransferAsync(command, token),
                cancellation.Token);
            if (!CanApplyResult(userId, cancellation))
            {
                return;
            }

            if (!result.Success)
            {
                if (result.FailureReason is CreateTreasuryTransferFailureReason.NotAuthenticated
                    or CreateTreasuryTransferFailureReason.Forbidden)
                {
                    ClearSummaryData();
                }

                OperationErrorMessage = result.FailureReason switch
                {
                    CreateTreasuryTransferFailureReason.InvalidAmount => PositiveAmountError,
                    CreateTreasuryTransferFailureReason.SameAccount =>
                        "Les comptes source et destination doivent être différents.",
                    CreateTreasuryTransferFailureReason.InvalidAccount =>
                        "Le compte sélectionné n'est pas valide.",
                    _ => OperationError
                };
                return;
            }

            ResetTransferForm();
            IsTransferFormVisible = false;
            if (await RefreshAfterWriteAsync(userId, cancellation.Token))
            {
                SuccessMessage = string.IsNullOrWhiteSpace(result.Reference)
                    ? "Transfert enregistré avec succès."
                    : $"Transfert {result.Reference} enregistré avec succès.";
            }
            else if (!_isStopped)
            {
                OperationErrorMessage = RefreshAfterWriteError;
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // Cancellation is expected when the owning Shell is being closed.
        }
        catch (Exception exception)
        {
            Trace.TraceError("Treasury transfer creation failed: {0}", exception);
            if (!_isStopped)
            {
                OperationErrorMessage = OperationError;
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

    private async Task<bool> RefreshAfterWriteAsync(long? userId, CancellationToken cancellationToken)
    {
        try
        {
            var result = await GetSummaryAsync(cancellationToken);
            if (_isStopped || cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (!CanAccessTreasury() || _currentUserSession.UserId != userId)
            {
                ClearSummaryData();
                return false;
            }

            if (!result.Success)
            {
                ClearSummaryData();
                return false;
            }

            ApplySummary(result);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Treasury refresh after write failed: {0}", exception);
            return false;
        }
    }

    private Task<TreasurySummaryResult> GetSummaryAsync(CancellationToken cancellationToken) =>
        _operationCoordinator.RunAsync(
            token => _treasuryService.GetSummaryAsync(token),
            cancellationToken);

    private bool CanAccessTreasury() =>
        _currentUserSession.IsAuthenticated
        && _currentUserSession.UserId.HasValue
        && _authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.Treasury);

    private bool CanApplyResult(long? userId, CancellationTokenSource cancellation)
    {
        if (_isStopped || cancellation.IsCancellationRequested)
        {
            return false;
        }

        if (CanAccessTreasury() && _currentUserSession.UserId == userId)
        {
            return true;
        }

        ClearSummaryData();
        OperationErrorMessage = null;
        SummaryErrorMessage = SummaryError;
        return false;
    }

    private void ApplySummary(TreasurySummaryResult result)
    {
        IsInitialized = result.IsInitialized;
        CashBalance = MoneyFormatter.FormatMinor(result.CashBalanceMinor);
        BankBalance = MoneyFormatter.FormatMinor(result.BankBalanceMinor);
        TotalTreasury = MoneyFormatter.FormatMinor(result.TotalTreasuryMinor);
        CashBalanceIsNegative = result.CashBalanceMinor < 0;
        BankBalanceIsNegative = result.BankBalanceMinor < 0;
        TotalTreasuryIsNegative = result.TotalTreasuryMinor < 0;
        OpeningDescription = result.IsInitialized
            ? result.OpeningDate is DateOnly openingDate
                ? $"Trésorerie initialisée le {openingDate:dd/MM/yyyy}."
                : "Trésorerie initialisée."
            : "Trésorerie non initialisée";
        HasData = true;
        SummaryErrorMessage = null;

        if (result.IsInitialized)
        {
            IsOpeningFormVisible = false;
        }
        else
        {
            IsTransferFormVisible = false;
        }
    }

    private void ClearSummaryData()
    {
        HasData = false;
        IsInitialized = false;
        CashBalance = string.Empty;
        BankBalance = string.Empty;
        TotalTreasury = string.Empty;
        CashBalanceIsNegative = false;
        BankBalanceIsNegative = false;
        TotalTreasuryIsNegative = false;
        OpeningDescription = string.Empty;
        IsOpeningFormVisible = false;
        IsTransferFormVisible = false;
    }

    private void ShowOpeningForm()
    {
        ResetOpeningForm();
        OperationErrorMessage = null;
        SuccessMessage = null;
        IsOpeningFormVisible = true;
    }

    private void CancelOpeningForm()
    {
        ResetOpeningForm();
        OperationErrorMessage = null;
        IsOpeningFormVisible = false;
    }

    private void ShowTransferForm()
    {
        ResetTransferForm();
        OperationErrorMessage = null;
        SuccessMessage = null;
        IsTransferFormVisible = true;
    }

    private void CancelTransferForm()
    {
        ResetTransferForm();
        OperationErrorMessage = null;
        IsTransferFormVisible = false;
    }

    private void ResetOpeningForm()
    {
        OpeningDate = _timeProvider.GetLocalNow().Date;
        OpeningCashAmount = string.Empty;
        OpeningBankAmount = string.Empty;
    }

    private void ResetTransferForm()
    {
        SelectedFromAccount = AccountOptions[0];
        SelectedToAccount = AccountOptions[1];
        TransferAmount = string.Empty;
        TransferObservation = string.Empty;
    }

    private void NotifyBusyStateChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        RaiseCommandCanExecuteChanged();
    }

    private void NotifySummaryStateChanged()
    {
        OnPropertyChanged(nameof(ShowInitializedState));
        OnPropertyChanged(nameof(ShowUninitializedState));
        RaiseCommandCanExecuteChanged();
    }

    private void RaiseCommandCanExecuteChanged()
    {
        _refreshRelayCommand?.RaiseCanExecuteChanged();
        _createOpeningBalanceRelayCommand?.RaiseCanExecuteChanged();
        _createTransferRelayCommand?.RaiseCanExecuteChanged();
        _showOpeningFormRelayCommand?.RaiseCanExecuteChanged();
        _cancelOpeningFormRelayCommand?.RaiseCanExecuteChanged();
        _showTransferFormRelayCommand?.RaiseCanExecuteChanged();
        _cancelTransferFormRelayCommand?.RaiseCanExecuteChanged();
    }
}
