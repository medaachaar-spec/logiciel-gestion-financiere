using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Initialization;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class InitializationViewModel : ViewModelBase
{
    private readonly IApplicationInitializationService _initializationService;
    private readonly ITreasuryService _treasuryService;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly RelayCommand _requestSaveRelayCommand;
    private readonly RelayCommand _cancelConfirmationRelayCommand;
    private readonly AsyncRelayCommand _confirmSaveRelayCommand;
    private CancellationTokenSource _lifetimeCancellation = new();
    private DateTime? _openingDate = DateTime.Today;
    private string _cashAmount = "0,00";
    private string _bankAmount = "0,00";
    private string? _errorMessage;
    private string? _successMessage;
    private bool _isBusy;
    private bool _isConfirmationVisible;
    private bool _isInitializationComplete;
    private bool _hasOpeningBalances;
    private bool _isStopped;
    private long _pendingCashMinor;
    private long _pendingBankMinor;
    private DateOnly _pendingOpeningDate;
    private Task _pendingOperation = Task.CompletedTask;

    public InitializationViewModel(
        IApplicationInitializationService initializationService,
        ITreasuryService treasuryService,
        ICurrentUserSession currentUserSession,
        DashboardOperationCoordinator operationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(initializationService);
        ArgumentNullException.ThrowIfNull(treasuryService);
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(operationCoordinator);
        _initializationService = initializationService;
        _treasuryService = treasuryService;
        _currentUserSession = currentUserSession;
        _operationCoordinator = operationCoordinator;

        _requestSaveRelayCommand = new RelayCommand(
            _ => RequestSave(),
            _ => CanEdit && !IsBusy && !IsConfirmationVisible);
        _cancelConfirmationRelayCommand = new RelayCommand(
            _ => IsConfirmationVisible = false,
            _ => IsConfirmationVisible && !IsBusy);
        _confirmSaveRelayCommand = new AsyncRelayCommand(
            _ => Track(ConfirmSaveAsync()),
            _ => IsConfirmationVisible && CanEdit && !IsBusy);
        RequestSaveCommand = _requestSaveRelayCommand;
        CancelConfirmationCommand = _cancelConfirmationRelayCommand;
        ConfirmSaveCommand = _confirmSaveRelayCommand;
    }

    public event EventHandler? InitializationCompleted;

    public event EventHandler? AuthenticationInvalidated;

    public ICommand RequestSaveCommand { get; }

    public ICommand CancelConfirmationCommand { get; }

    public ICommand ConfirmSaveCommand { get; }

    public DateTime? OpeningDate
    {
        get => _openingDate;
        set
        {
            if (SetProperty(ref _openingDate, value))
            {
                ClearFeedback();
            }
        }
    }

    public string CashAmount
    {
        get => _cashAmount;
        set
        {
            if (SetProperty(ref _cashAmount, value))
            {
                ClearFeedback();
            }
        }
    }

    public string BankAmount
    {
        get => _bankAmount;
        set
        {
            if (SetProperty(ref _bankAmount, value))
            {
                ClearFeedback();
            }
        }
    }

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

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

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

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(SaveButtonText));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string SaveButtonText => IsBusy ? "Enregistrement…" : "Enregistrer les soldes";

    public bool IsConfirmationVisible
    {
        get => _isConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isConfirmationVisible, value))
            {
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string ConfirmationCashAmount => MoneyFormatter.FormatMinor(_pendingCashMinor);

    public string ConfirmationBankAmount => MoneyFormatter.FormatMinor(_pendingBankMinor);

    public string ConfirmationDate => $"Date d'ouverture : {_pendingOpeningDate:dd/MM/yyyy}";

    public bool IsInitializationComplete
    {
        get => _isInitializationComplete;
        private set => SetProperty(ref _isInitializationComplete, value);
    }

    public bool HasOpeningBalances
    {
        get => _hasOpeningBalances;
        private set
        {
            if (SetProperty(ref _hasOpeningBalances, value))
            {
                OnPropertyChanged(nameof(CanEdit));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool CanEdit => !HasOpeningBalances && !_isStopped;

    public Task LoadAsync(CancellationToken cancellationToken = default) =>
        Track(RefreshStateAsync(cancellationToken));

    public void Stop()
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;
        _lifetimeCancellation.Cancel();
        IsConfirmationVisible = false;
        RaiseCommandCanExecuteChanged();
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
        }
    }

    private void RequestSave()
    {
        ClearFeedback();
        if (OpeningDate is not DateTime openingDate)
        {
            ErrorMessage = "Veuillez sélectionner la date d'ouverture.";
            return;
        }

        if (!MoneyParser.TryParseSignedMinorUnits(CashAmount, out _pendingCashMinor))
        {
            ErrorMessage = "Le solde initial de caisse est invalide.";
            return;
        }

        if (!MoneyParser.TryParseSignedMinorUnits(BankAmount, out _pendingBankMinor))
        {
            ErrorMessage = "Le solde initial de banque est invalide.";
            return;
        }

        _pendingOpeningDate = DateOnly.FromDateTime(openingDate);
        OnPropertyChanged(nameof(ConfirmationCashAmount));
        OnPropertyChanged(nameof(ConfirmationBankAmount));
        OnPropertyChanged(nameof(ConfirmationDate));
        IsConfirmationVisible = true;
    }

    private async Task ConfirmSaveAsync()
    {
        if (!TryCaptureResponsibleSession(out var identity) || !CanEdit || IsBusy)
        {
            FailClosed();
            return;
        }

        ClearFeedback();
        IsBusy = true;
        try
        {
            var result = await _operationCoordinator.RunAsync(
                token => _treasuryService.CreateOpeningBalanceAsync(
                    new CreateTreasuryOpeningBalanceCommand(
                        _pendingOpeningDate,
                        _pendingCashMinor,
                        _pendingBankMinor),
                    token),
                _lifetimeCancellation.Token);

            if (!IsSameSession(identity))
            {
                FailClosed();
                return;
            }

            if (!result.Success)
            {
                if (result.FailureReason is CreateTreasuryOpeningBalanceFailureReason.NotAuthenticated
                    or CreateTreasuryOpeningBalanceFailureReason.Forbidden)
                {
                    FailClosed();
                    return;
                }

                if (result.FailureReason == CreateTreasuryOpeningBalanceFailureReason.AlreadyInitialized)
                {
                    IsConfirmationVisible = false;
                    await RefreshStateAsync();
                    if (IsInitializationComplete)
                    {
                        SuccessMessage = "Les soldes d'ouverture ont déjà été enregistrés.";
                        InitializationCompleted?.Invoke(this, EventArgs.Empty);
                    }
                    else
                    {
                        ErrorMessage = "Les soldes d'ouverture ont déjà été enregistrés dans une autre session.";
                    }
                    return;
                }

                ErrorMessage = "Impossible d'enregistrer les soldes d'ouverture.";
                return;
            }

            IsConfirmationVisible = false;
            await RefreshStateAsync();
            if (IsInitializationComplete)
            {
                SuccessMessage = "Les soldes d'ouverture ont été enregistrés.";
                InitializationCompleted?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                ErrorMessage = "L'initialisation reste incomplète. Réessayez.";
            }
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Opening balance initialization failed unexpectedly: {0}", exception);
            ErrorMessage = "Une erreur est survenue pendant l'enregistrement.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RefreshStateAsync(CancellationToken cancellationToken = default)
    {
        if (!TryCaptureResponsibleSession(out var identity) || _isStopped)
        {
            FailClosed();
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _lifetimeCancellation.Token);
        try
        {
            var state = await _operationCoordinator.RunAsync(
                token => _initializationService.GetStateAsync(token),
                linkedCancellation.Token);
            if (!IsSameSession(identity))
            {
                FailClosed();
                return;
            }

            HasOpeningBalances = state.HasOpeningBalances;
            IsInitializationComplete = state.IsInitializationComplete;
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Initialization state refresh failed unexpectedly: {0}", exception);
            ErrorMessage = "Impossible de vérifier l'état de l'initialisation.";
        }
    }

    private Task Track(Task operation)
    {
        _pendingOperation = operation;
        return operation;
    }

    private bool TryCaptureResponsibleSession(out SessionIdentity identity)
    {
        if (_currentUserSession.IsAuthenticated
            && _currentUserSession.UserId is long userId
            && _currentUserSession.Role == UserRole.Responsable)
        {
            identity = new SessionIdentity(userId, _currentUserSession.Username);
            return true;
        }

        identity = default;
        return false;
    }

    private bool IsSameSession(SessionIdentity identity) =>
        _currentUserSession.IsAuthenticated
        && _currentUserSession.Role == UserRole.Responsable
        && _currentUserSession.UserId == identity.UserId
        && string.Equals(_currentUserSession.Username, identity.Username, StringComparison.Ordinal);

    private void FailClosed()
    {
        HasOpeningBalances = false;
        IsInitializationComplete = false;
        IsConfirmationVisible = false;
        CashAmount = string.Empty;
        BankAmount = string.Empty;
        SuccessMessage = null;
        ErrorMessage = "Votre session n'autorise plus cette opération.";
        AuthenticationInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void ClearFeedback()
    {
        ErrorMessage = null;
        SuccessMessage = null;
    }

    private void RaiseCommandCanExecuteChanged()
    {
        _requestSaveRelayCommand.RaiseCanExecuteChanged();
        _cancelConfirmationRelayCommand.RaiseCanExecuteChanged();
        _confirmSaveRelayCommand.RaiseCanExecuteChanged();
    }

    private readonly record struct SessionIdentity(long UserId, string? Username);
}
