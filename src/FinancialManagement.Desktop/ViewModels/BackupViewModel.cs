using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Backup;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class BackupViewModel : ViewModelBase
{
    private readonly IDatabaseBackupService _backupService;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IBackupDialogService _dialogService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly AsyncRelayCommand _createLocalBackupCommand;
    private readonly AsyncRelayCommand _createExternalBackupCommand;
    private readonly RelayCommand _selectRestoreBackupCommand;
    private readonly AsyncRelayCommand _restoreBackupCommand;
    private CancellationTokenSource _lifetimeCancellation = new();
    private Task _pendingOperation = Task.CompletedTask;
    private bool _isActive;
    private bool _isBusy;
    private string? _selectedRestorePath;
    private string? _successMessage;
    private string? _errorMessage;

    public BackupViewModel(
        IDatabaseBackupService backupService,
        ICurrentUserSession currentUserSession,
        IBackupDialogService dialogService,
        DashboardOperationCoordinator operationCoordinator)
    {
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(dialogService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);

        _backupService = backupService;
        _currentUserSession = currentUserSession;
        _dialogService = dialogService;
        _operationCoordinator = operationCoordinator;
        _createLocalBackupCommand = new AsyncRelayCommand(
            _ => Track(CreateLocalBackupAsync()),
            _ => CanOperate);
        _createExternalBackupCommand = new AsyncRelayCommand(
            _ => Track(CreateExternalBackupAsync()),
            _ => CanOperate);
        _selectRestoreBackupCommand = new RelayCommand(
            _ => SelectRestoreBackup(),
            _ => CanOperate);
        _restoreBackupCommand = new AsyncRelayCommand(
            _ => Track(RestoreBackupAsync()),
            _ => CanOperate && !string.IsNullOrWhiteSpace(SelectedRestorePath));

        CreateLocalBackupCommand = _createLocalBackupCommand;
        CreateExternalBackupCommand = _createExternalBackupCommand;
        SelectRestoreBackupCommand = _selectRestoreBackupCommand;
        RestoreBackupCommand = _restoreBackupCommand;
    }

    public event EventHandler? AuthenticationInvalidated;

    public ICommand CreateLocalBackupCommand { get; }

    public ICommand CreateExternalBackupCommand { get; }

    public ICommand SelectRestoreBackupCommand { get; }

    public ICommand RestoreBackupCommand { get; }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanOperate));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool CanOperate => _isActive && !IsBusy && IsResponsibleSession();

    public string? SelectedRestorePath
    {
        get => _selectedRestorePath;
        private set
        {
            if (SetProperty(ref _selectedRestorePath, value))
            {
                OnPropertyChanged(nameof(HasSelectedRestorePath));
                _restoreBackupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedRestorePath => !string.IsNullOrWhiteSpace(SelectedRestorePath);

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

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            if (SetProperty(ref _errorMessage, value))
            {
                OnPropertyChanged(nameof(HasErrorMessage));
            }
        }
    }

    public bool HasErrorMessage => !string.IsNullOrWhiteSpace(ErrorMessage);

    public void Activate()
    {
        if (_isActive)
        {
            return;
        }

        _lifetimeCancellation.Dispose();
        _lifetimeCancellation = new CancellationTokenSource();
        _isActive = true;
        RaiseCommandCanExecuteChanged();
        if (!IsResponsibleSession())
        {
            FailClosed();
        }
    }

    public void Deactivate()
    {
        _isActive = false;
        _lifetimeCancellation.Cancel();
        SelectedRestorePath = null;
        SuccessMessage = null;
        ErrorMessage = null;
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

    private async Task CreateLocalBackupAsync()
    {
        if (!TryCaptureSession(out var identity))
        {
            FailClosed();
            return;
        }

        ClearFeedback();
        IsBusy = true;
        try
        {
            var result = await _operationCoordinator.RunAsync(
                token => _backupService.CreateLocalBackupAsync(token),
                _lifetimeCancellation.Token);
            if (!IsSameSession(identity))
            {
                FailClosed();
                return;
            }

            if (!result.Success)
            {
                HandleFailure(result.FailureReason, "Impossible de créer la sauvegarde.");
                return;
            }

            SuccessMessage = "Sauvegarde locale créée : " + result.BackupPath;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Local backup failed unexpectedly: {0}", exception);
            ErrorMessage = "Impossible de créer la sauvegarde.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task CreateExternalBackupAsync()
    {
        if (!TryCaptureSession(out var identity))
        {
            FailClosed();
            return;
        }

        var destination = _dialogService.SelectExternalDirectory();
        if (string.IsNullOrWhiteSpace(destination))
        {
            return;
        }

        ClearFeedback();
        IsBusy = true;
        try
        {
            var result = await _operationCoordinator.RunAsync(
                token => _backupService.CreateExternalBackupAsync(new(destination), token),
                _lifetimeCancellation.Token);
            if (!IsSameSession(identity))
            {
                FailClosed();
                return;
            }

            if (!result.Success)
            {
                HandleFailure(result.FailureReason, "Impossible de créer la sauvegarde externe.");
                return;
            }

            SuccessMessage = "Sauvegarde externe créée : " + result.BackupPath;
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("External backup failed unexpectedly: {0}", exception);
            ErrorMessage = "Impossible de créer la sauvegarde externe.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void SelectRestoreBackup()
    {
        if (!IsResponsibleSession())
        {
            FailClosed();
            return;
        }

        var selectedPath = _dialogService.SelectBackupFile();
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SelectedRestorePath = selectedPath;
            ClearFeedback();
        }
    }

    private async Task RestoreBackupAsync()
    {
        if (!TryCaptureSession(out var identity)
            || string.IsNullOrWhiteSpace(SelectedRestorePath))
        {
            FailClosed();
            return;
        }

        var sourcePath = SelectedRestorePath;
        if (!_dialogService.ConfirmRestore(sourcePath))
        {
            return;
        }

        ClearFeedback();
        IsBusy = true;
        try
        {
            var result = await _operationCoordinator.RunAsync(
                token => _backupService.RestoreAsync(new(sourcePath), token),
                _lifetimeCancellation.Token);
            if (result.Success)
            {
                SuccessMessage = "La restauration est terminée.";
                _dialogService.ShowRestoreSucceeded();
                AuthenticationInvalidated?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!IsSameSession(identity))
            {
                FailClosed();
                return;
            }

            HandleFailure(
                result.FailureReason,
                "La restauration n'a pas pu être terminée. Les données actuelles ont été préservées.");
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Database restore failed unexpectedly: {0}", exception);
            ErrorMessage = "La restauration n'a pas pu être terminée. Reconnectez-vous avant toute autre opération.";
            AuthenticationInvalidated?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void HandleFailure(DatabaseBackupFailureReason reason, string fallback)
    {
        if (reason is DatabaseBackupFailureReason.NotAuthenticated
            or DatabaseBackupFailureReason.Forbidden)
        {
            FailClosed();
            return;
        }

        ErrorMessage = reason switch
        {
            DatabaseBackupFailureReason.DestinationDirectoryNotFound =>
                "L'emplacement sélectionné n'est plus disponible.",
            DatabaseBackupFailureReason.BackupFileNotFound =>
                "La sauvegarde sélectionnée est introuvable.",
            DatabaseBackupFailureReason.InvalidBackup =>
                "La sauvegarde sélectionnée n'est pas valide.",
            DatabaseBackupFailureReason.IncompatibleBackup =>
                "La sauvegarde sélectionnée n'est pas compatible avec cette version.",
            _ => fallback
        };
    }

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

    private bool IsSameSession(SessionIdentity identity) =>
        IsResponsibleSession()
        && _currentUserSession.UserId == identity.UserId
        && string.Equals(_currentUserSession.Username, identity.Username, StringComparison.Ordinal);

    private bool IsResponsibleSession() =>
        _currentUserSession.IsAuthenticated
        && _currentUserSession.Role == UserRole.Responsable;

    private void FailClosed()
    {
        _isActive = false;
        _lifetimeCancellation.Cancel();
        SelectedRestorePath = null;
        SuccessMessage = null;
        ErrorMessage = "Votre session n'autorise plus les sauvegardes et restaurations.";
        RaiseCommandCanExecuteChanged();
        AuthenticationInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void ClearFeedback()
    {
        SuccessMessage = null;
        ErrorMessage = null;
    }

    private void RaiseCommandCanExecuteChanged()
    {
        _createLocalBackupCommand.RaiseCanExecuteChanged();
        _createExternalBackupCommand.RaiseCanExecuteChanged();
        _selectRestoreBackupCommand.RaiseCanExecuteChanged();
        _restoreBackupCommand.RaiseCanExecuteChanged();
    }

    private Task Track(Task operation)
    {
        _pendingOperation = operation;
        return operation;
    }

    private readonly record struct SessionIdentity(long UserId, string? Username);
}
