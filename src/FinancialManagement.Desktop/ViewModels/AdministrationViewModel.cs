using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Administration;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class AdministrationViewModel : ViewModelBase
{
    private readonly IUserAdministrationService _administrationService;
    private readonly ICurrentUserSession _currentUserSession;
    private readonly IAuthenticationService _authenticationService;
    private readonly DashboardOperationCoordinator _operationCoordinator;
    private readonly AsyncRelayCommand _refreshRelayCommand;
    private readonly RelayCommand _showCreateEmployeeRelayCommand;
    private readonly RelayCommand _cancelCreateEmployeeRelayCommand;
    private readonly AsyncRelayCommand _createEmployeeRelayCommand;
    private readonly AsyncRelayCommand _savePermissionsRelayCommand;
    private readonly RelayCommand _requestSetActiveRelayCommand;
    private readonly RelayCommand _cancelSetActiveRelayCommand;
    private readonly AsyncRelayCommand _confirmSetActiveRelayCommand;
    private readonly RelayCommand _showResetPasswordRelayCommand;
    private readonly RelayCommand _cancelResetPasswordRelayCommand;
    private readonly AsyncRelayCommand _resetPasswordRelayCommand;
    private CancellationTokenSource _lifetimeCancellation = new();
    private CancellationTokenSource? _operationCancellation;
    private Task _pendingOperation = Task.CompletedTask;
    private long _requestVersion;
    private bool _isStopped = true;
    private bool _isLoading;
    private bool _isWriting;
    private bool _hasLoaded;
    private AdministrationUserItemViewModel? _selectedUser;
    private string? _errorMessage;
    private string? _successMessage;
    private bool _isCreateEmployeeVisible;
    private string _employeeFullName = string.Empty;
    private string _employeeUsername = string.Empty;
    private string? _createEmployeeError;
    private bool _isSetActiveConfirmationVisible;
    private AdministrationUserItemViewModel? _pendingActiveUser;
    private bool _pendingActiveValue;
    private string? _setActiveError;
    private bool _isResetPasswordVisible;
    private string? _resetPasswordError;

    public AdministrationViewModel(
        IUserAdministrationService administrationService,
        ICurrentUserSession currentUserSession,
        IAuthenticationService authenticationService,
        DashboardOperationCoordinator operationCoordinator,
        InitialCustomerReceivablesViewModel customerReceivables,
        InitialSupplierPayablesViewModel supplierPayables)
    {
        ArgumentNullException.ThrowIfNull(administrationService);
        ArgumentNullException.ThrowIfNull(currentUserSession);
        ArgumentNullException.ThrowIfNull(authenticationService);
        ArgumentNullException.ThrowIfNull(operationCoordinator);
        ArgumentNullException.ThrowIfNull(customerReceivables);
        ArgumentNullException.ThrowIfNull(supplierPayables);
        _administrationService = administrationService;
        _currentUserSession = currentUserSession;
        _authenticationService = authenticationService;
        _operationCoordinator = operationCoordinator;
        CustomerReceivables = customerReceivables;
        SupplierPayables = supplierPayables;
        CustomerReceivables.AuthenticationInvalidated += Child_AuthenticationInvalidated;
        SupplierPayables.AuthenticationInvalidated += Child_AuthenticationInvalidated;

        foreach (var module in Enum.GetValues<PermissionModule>())
        {
            PermissionRows.Add(new EmployeePermissionRowViewModel(module));
        }

        _refreshRelayCommand = new AsyncRelayCommand(
            _ => Track(LoadUsersAsync()),
            _ => CanOperate);
        _showCreateEmployeeRelayCommand = new RelayCommand(
            _ => ShowCreateEmployee(),
            _ => CanOperate && !IsOverlayVisible);
        _cancelCreateEmployeeRelayCommand = new RelayCommand(
            _ => CloseCreateEmployee(),
            _ => IsCreateEmployeeVisible && !IsWriting);
        _createEmployeeRelayCommand = new AsyncRelayCommand(
            parameter => Track(CreateEmployeeAsync(parameter as CredentialSubmission)),
            _ => IsCreateEmployeeVisible && !IsBusy);
        _savePermissionsRelayCommand = new AsyncRelayCommand(
            _ => Track(SavePermissionsAsync()),
            _ => CanEditPermissions && !IsBusy && !IsOverlayVisible);
        _requestSetActiveRelayCommand = new RelayCommand(
            _ => RequestSetActive(),
            _ => SelectedUser is not null && CanOperate && !IsOverlayVisible);
        _cancelSetActiveRelayCommand = new RelayCommand(
            _ => CloseSetActiveConfirmation(),
            _ => IsSetActiveConfirmationVisible && !IsWriting);
        _confirmSetActiveRelayCommand = new AsyncRelayCommand(
            _ => Track(ConfirmSetActiveAsync()),
            _ => IsSetActiveConfirmationVisible && PendingActiveUser is not null && !IsBusy);
        _showResetPasswordRelayCommand = new RelayCommand(
            _ => ShowResetPassword(),
            _ => SelectedUser is not null && CanOperate && !IsOverlayVisible);
        _cancelResetPasswordRelayCommand = new RelayCommand(
            _ => CloseResetPassword(),
            _ => IsResetPasswordVisible && !IsWriting);
        _resetPasswordRelayCommand = new AsyncRelayCommand(
            parameter => Track(ResetPasswordAsync(parameter as CredentialSubmission)),
            _ => IsResetPasswordVisible && SelectedUser is not null && !IsBusy);

        RefreshCommand = _refreshRelayCommand;
        ShowCreateEmployeeCommand = _showCreateEmployeeRelayCommand;
        CancelCreateEmployeeCommand = _cancelCreateEmployeeRelayCommand;
        CreateEmployeeCommand = _createEmployeeRelayCommand;
        SavePermissionsCommand = _savePermissionsRelayCommand;
        RequestSetActiveCommand = _requestSetActiveRelayCommand;
        CancelSetActiveCommand = _cancelSetActiveRelayCommand;
        ConfirmSetActiveCommand = _confirmSetActiveRelayCommand;
        ShowResetPasswordCommand = _showResetPasswordRelayCommand;
        CancelResetPasswordCommand = _cancelResetPasswordRelayCommand;
        ResetPasswordCommand = _resetPasswordRelayCommand;
    }

    public event EventHandler? AuthenticationInvalidated;

    public ObservableCollection<AdministrationUserItemViewModel> Users { get; } = [];

    public ObservableCollection<EmployeePermissionRowViewModel> PermissionRows { get; } = [];

    public InitialCustomerReceivablesViewModel CustomerReceivables { get; }

    public InitialSupplierPayablesViewModel SupplierPayables { get; }

    public ICommand RefreshCommand { get; }

    public ICommand ShowCreateEmployeeCommand { get; }

    public ICommand CancelCreateEmployeeCommand { get; }

    public AsyncRelayCommand CreateEmployeeCommand { get; }

    public ICommand SavePermissionsCommand { get; }

    public ICommand RequestSetActiveCommand { get; }

    public ICommand CancelSetActiveCommand { get; }

    public ICommand ConfirmSetActiveCommand { get; }

    public ICommand ShowResetPasswordCommand { get; }

    public ICommand CancelResetPasswordCommand { get; }

    public AsyncRelayCommand ResetPasswordCommand { get; }

    public AdministrationUserItemViewModel? SelectedUser
    {
        get => _selectedUser;
        set
        {
            if (SetProperty(ref _selectedUser, value))
            {
                PopulatePermissionRows();
                OnPropertyChanged(nameof(HasSelectedUser));
                OnPropertyChanged(nameof(CanEditPermissions));
                OnPropertyChanged(nameof(SelectedUserDescription));
                OnPropertyChanged(nameof(SetActiveButtonLabel));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public bool HasSelectedUser => SelectedUser is not null;

    public bool CanEditPermissions => SelectedUser?.Role == UserRole.Employe;

    public string SelectedUserDescription => SelectedUser is null
        ? "Sélectionnez un utilisateur pour gérer ses accès."
        : $"{SelectedUser.FullName} — {SelectedUser.RoleLabel} — {SelectedUser.StatusLabel}";

    public string SetActiveButtonLabel => SelectedUser?.IsActive == true ? "Désactiver" : "Réactiver";

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                NotifyBusyChanged();
            }
        }
    }

    public bool IsWriting
    {
        get => _isWriting;
        private set
        {
            if (SetProperty(ref _isWriting, value))
            {
                NotifyBusyChanged();
            }
        }
    }

    public bool IsBusy => IsLoading || IsWriting;

    public bool CanOperate => !_isStopped && !IsBusy && IsResponsibleSession();

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

    public bool IsCreateEmployeeVisible
    {
        get => _isCreateEmployeeVisible;
        private set
        {
            if (SetProperty(ref _isCreateEmployeeVisible, value))
            {
                OnPropertyChanged(nameof(IsOverlayVisible));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string EmployeeFullName
    {
        get => _employeeFullName;
        set
        {
            if (SetProperty(ref _employeeFullName, value))
            {
                CreateEmployeeError = null;
            }
        }
    }

    public string EmployeeUsername
    {
        get => _employeeUsername;
        set
        {
            if (SetProperty(ref _employeeUsername, value))
            {
                CreateEmployeeError = null;
            }
        }
    }

    public string? CreateEmployeeError
    {
        get => _createEmployeeError;
        private set
        {
            if (SetProperty(ref _createEmployeeError, value))
            {
                OnPropertyChanged(nameof(HasCreateEmployeeError));
            }
        }
    }

    public bool HasCreateEmployeeError => !string.IsNullOrWhiteSpace(CreateEmployeeError);

    public bool IsSetActiveConfirmationVisible
    {
        get => _isSetActiveConfirmationVisible;
        private set
        {
            if (SetProperty(ref _isSetActiveConfirmationVisible, value))
            {
                OnPropertyChanged(nameof(IsOverlayVisible));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public AdministrationUserItemViewModel? PendingActiveUser
    {
        get => _pendingActiveUser;
        private set
        {
            if (SetProperty(ref _pendingActiveUser, value))
            {
                OnPropertyChanged(nameof(SetActiveConfirmationTitle));
                OnPropertyChanged(nameof(SetActiveConfirmationDescription));
                OnPropertyChanged(nameof(SetActiveConfirmationButton));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string SetActiveConfirmationTitle => _pendingActiveValue ? "Réactiver cet utilisateur ?" : "Désactiver cet utilisateur ?";

    public string SetActiveConfirmationDescription => PendingActiveUser is null
        ? string.Empty
        : _pendingActiveValue
            ? $"{PendingActiveUser.FullName} pourra de nouveau se connecter."
            : $"{PendingActiveUser.FullName} ne pourra plus se connecter.";

    public string SetActiveConfirmationButton => _pendingActiveValue ? "Réactiver" : "Désactiver";

    public string? SetActiveError
    {
        get => _setActiveError;
        private set
        {
            if (SetProperty(ref _setActiveError, value))
            {
                OnPropertyChanged(nameof(HasSetActiveError));
            }
        }
    }

    public bool HasSetActiveError => !string.IsNullOrWhiteSpace(SetActiveError);

    public bool IsResetPasswordVisible
    {
        get => _isResetPasswordVisible;
        private set
        {
            if (SetProperty(ref _isResetPasswordVisible, value))
            {
                OnPropertyChanged(nameof(IsOverlayVisible));
                RaiseCommandCanExecuteChanged();
            }
        }
    }

    public string ResetPasswordTitle => SelectedUser is null
        ? "Réinitialiser le mot de passe"
        : $"Nouveau mot de passe — {SelectedUser.Username}";

    public string? ResetPasswordError
    {
        get => _resetPasswordError;
        private set
        {
            if (SetProperty(ref _resetPasswordError, value))
            {
                OnPropertyChanged(nameof(HasResetPasswordError));
            }
        }
    }

    public bool HasResetPasswordError => !string.IsNullOrWhiteSpace(ResetPasswordError);

    public bool IsOverlayVisible => IsCreateEmployeeVisible || IsSetActiveConfirmationVisible || IsResetPasswordVisible;

    public Task LoadAsync(CancellationToken cancellationToken = default)
    {
        Reactivate();
        return _hasLoaded ? Task.CompletedTask : Track(LoadUsersAsync(cancellationToken));
    }

    public void Deactivate()
    {
        _isStopped = true;
        Interlocked.Increment(ref _requestVersion);
        _operationCancellation?.Cancel();
        _lifetimeCancellation.Cancel();
        CustomerReceivables.Deactivate();
        SupplierPayables.Deactivate();
        CloseAllOverlays();
        ClearProtectedData();
        RaiseCommandCanExecuteChanged();
    }

    public async Task StopAsync()
    {
        Deactivate();
        try
        {
            await Task.WhenAll(
                _pendingOperation,
                CustomerReceivables.StopAsync(),
                SupplierPayables.StopAsync());
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoadUsersAsync(CancellationToken cancellationToken = default)
    {
        if (!TryCaptureSession(out var identity) || _isStopped)
        {
            FailClosed();
            return;
        }

        var selectedId = SelectedUser?.Id;
        var version = BeginOperation(cancellationToken, out var linkedCancellation);
        IsLoading = true;
        ErrorMessage = null;
        try
        {
            using (linkedCancellation)
            {
                var result = await _operationCoordinator.RunAsync(
                    token => _administrationService.GetUsersAsync(token),
                    linkedCancellation.Token);
                if (!CanAcceptResult(version, identity))
                {
                    return;
                }

                if (!result.Success)
                {
                    if (IsAuthenticationFailure(result.FailureReason))
                    {
                        FailClosed();
                        return;
                    }

                    ErrorMessage = MapFailure(result.FailureReason, "Impossible de charger les utilisateurs.");
                    return;
                }

                Users.Clear();
                foreach (var item in result.Items)
                {
                    Users.Add(new AdministrationUserItemViewModel(item));
                }

                SelectedUser = selectedId.HasValue
                    ? Users.FirstOrDefault(user => user.Id == selectedId.Value)
                    : Users.FirstOrDefault();
                _hasLoaded = true;
            }
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("User administration list failed unexpectedly: {0}", exception);
            if (CanAcceptResult(version, identity))
            {
                ErrorMessage = "Impossible de charger les utilisateurs.";
            }
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task CreateEmployeeAsync(CredentialSubmission? credentials)
    {
        CreateEmployeeError = null;
        if (credentials is null || !TryCaptureSession(out var identity) || _isStopped)
        {
            FailClosed();
            return;
        }

        var fullName = EmployeeFullName.Trim();
        var username = EmployeeUsername.Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            CreateEmployeeError = "Veuillez renseigner le nom complet.";
            return;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            CreateEmployeeError = "Veuillez renseigner le nom d'utilisateur.";
            return;
        }

        if (string.IsNullOrEmpty(credentials.Password))
        {
            CreateEmployeeError = "Veuillez renseigner le mot de passe.";
            return;
        }

        if (!string.Equals(credentials.Password, credentials.Confirmation, StringComparison.Ordinal))
        {
            CreateEmployeeError = "Les mots de passe ne correspondent pas.";
            return;
        }

        var version = BeginOperation(default, out var linkedCancellation);
        IsWriting = true;
        try
        {
            using (linkedCancellation)
            {
                var result = await _operationCoordinator.RunAsync(
                    token => _administrationService.CreateEmployeeAsync(
                        new FinancialManagement.Application.Administration.CreateEmployeeCommand(
                            fullName,
                            username,
                            credentials.Password),
                        token),
                    linkedCancellation.Token);
                if (!CanAcceptResult(version, identity))
                {
                    return;
                }

                if (!result.Success)
                {
                    if (IsAuthenticationFailure(result.FailureReason))
                    {
                        FailClosed();
                        return;
                    }

                    CreateEmployeeError = MapFailure(result.FailureReason, "Impossible de créer l'employé.");
                    return;
                }

                CloseCreateEmployee();
                SuccessMessage = "L'employé a été créé.";
                _hasLoaded = false;
                await LoadUsersAsync();
            }
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Employee creation failed unexpectedly: {0}", exception);
            if (CanAcceptResult(version, identity))
            {
                CreateEmployeeError = "Impossible de créer l'employé.";
            }
        }
        finally
        {
            IsWriting = false;
        }
    }

    private async Task SavePermissionsAsync()
    {
        if (!TryCaptureSession(out var identity) || SelectedUser is not { Role: UserRole.Employe } user)
        {
            FailClosed();
            return;
        }

        var version = BeginOperation(default, out var linkedCancellation);
        IsWriting = true;
        ErrorMessage = null;
        try
        {
            using (linkedCancellation)
            {
                foreach (var permission in PermissionRows.Select(row => row.ToInput()))
                {
                    var result = await _operationCoordinator.RunAsync(
                        token => _administrationService.SetPermissionAsync(user.Id, permission, token),
                        linkedCancellation.Token);
                    if (!CanAcceptResult(version, identity))
                    {
                        return;
                    }

                    if (!result.Success)
                    {
                        if (IsAuthenticationFailure(result.FailureReason))
                        {
                            FailClosed();
                            return;
                        }

                        ErrorMessage = MapFailure(result.FailureReason, "Impossible d'enregistrer les permissions.");
                        return;
                    }
                }

                SuccessMessage = "Les permissions ont été enregistrées.";
                _hasLoaded = false;
                await LoadUsersAsync();
            }
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Permission update failed unexpectedly: {0}", exception);
            if (CanAcceptResult(version, identity))
            {
                ErrorMessage = "Impossible d'enregistrer les permissions.";
            }
        }
        finally
        {
            IsWriting = false;
        }
    }

    private void RequestSetActive()
    {
        if (SelectedUser is null)
        {
            return;
        }

        PendingActiveUser = SelectedUser;
        _pendingActiveValue = !SelectedUser.IsActive;
        OnPropertyChanged(nameof(SetActiveConfirmationTitle));
        OnPropertyChanged(nameof(SetActiveConfirmationDescription));
        OnPropertyChanged(nameof(SetActiveConfirmationButton));
        SetActiveError = null;
        IsSetActiveConfirmationVisible = true;
    }

    private async Task ConfirmSetActiveAsync()
    {
        if (!TryCaptureSession(out var identity) || PendingActiveUser is not { } user)
        {
            FailClosed();
            return;
        }

        var targetActive = _pendingActiveValue;
        var version = BeginOperation(default, out var linkedCancellation);
        IsWriting = true;
        SetActiveError = null;
        try
        {
            using (linkedCancellation)
            {
                var result = await _operationCoordinator.RunAsync(
                    token => _administrationService.SetActiveAsync(user.Id, targetActive, token),
                    linkedCancellation.Token);
                if (!CanAcceptResult(version, identity))
                {
                    return;
                }

                if (!result.Success)
                {
                    if (IsAuthenticationFailure(result.FailureReason))
                    {
                        FailClosed();
                        return;
                    }

                    SetActiveError = MapFailure(result.FailureReason, "Impossible de modifier l'état de l'utilisateur.");
                    return;
                }

                CloseSetActiveConfirmation();
                SuccessMessage = targetActive ? "L'utilisateur a été réactivé." : "L'utilisateur a été désactivé.";
                if (!targetActive && user.Id == identity.UserId)
                {
                    _authenticationService.Logout();
                    FailClosed();
                    return;
                }

                _hasLoaded = false;
                await LoadUsersAsync();
            }
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("User activation update failed unexpectedly: {0}", exception);
            if (CanAcceptResult(version, identity))
            {
                SetActiveError = "Impossible de modifier l'état de l'utilisateur.";
            }
        }
        finally
        {
            IsWriting = false;
        }
    }

    private async Task ResetPasswordAsync(CredentialSubmission? credentials)
    {
        ResetPasswordError = null;
        if (credentials is null || !TryCaptureSession(out var identity) || SelectedUser is not { } user)
        {
            FailClosed();
            return;
        }

        if (string.IsNullOrEmpty(credentials.Password))
        {
            ResetPasswordError = "Veuillez renseigner le nouveau mot de passe.";
            return;
        }

        if (!string.Equals(credentials.Password, credentials.Confirmation, StringComparison.Ordinal))
        {
            ResetPasswordError = "Les mots de passe ne correspondent pas.";
            return;
        }

        var version = BeginOperation(default, out var linkedCancellation);
        IsWriting = true;
        try
        {
            using (linkedCancellation)
            {
                var result = await _operationCoordinator.RunAsync(
                    token => _administrationService.ResetPasswordAsync(user.Id, credentials.Password, token),
                    linkedCancellation.Token);
                if (!CanAcceptResult(version, identity))
                {
                    return;
                }

                if (!result.Success)
                {
                    if (IsAuthenticationFailure(result.FailureReason))
                    {
                        FailClosed();
                        return;
                    }

                    ResetPasswordError = MapFailure(result.FailureReason, "Impossible de réinitialiser le mot de passe.");
                    return;
                }

                CloseResetPassword();
                SuccessMessage = "Le mot de passe a été réinitialisé.";
            }
        }
        catch (OperationCanceledException) when (linkedCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Password reset failed unexpectedly: {0}", exception);
            if (CanAcceptResult(version, identity))
            {
                ResetPasswordError = "Impossible de réinitialiser le mot de passe.";
            }
        }
        finally
        {
            IsWriting = false;
        }
    }

    private void ShowCreateEmployee()
    {
        CloseAllOverlays();
        EmployeeFullName = string.Empty;
        EmployeeUsername = string.Empty;
        CreateEmployeeError = null;
        IsCreateEmployeeVisible = true;
    }

    private void CloseCreateEmployee()
    {
        IsCreateEmployeeVisible = false;
        EmployeeFullName = string.Empty;
        EmployeeUsername = string.Empty;
        CreateEmployeeError = null;
    }

    private void ShowResetPassword()
    {
        CloseAllOverlays();
        ResetPasswordError = null;
        OnPropertyChanged(nameof(ResetPasswordTitle));
        IsResetPasswordVisible = true;
    }

    private void CloseResetPassword()
    {
        IsResetPasswordVisible = false;
        ResetPasswordError = null;
    }

    private void CloseSetActiveConfirmation()
    {
        IsSetActiveConfirmationVisible = false;
        PendingActiveUser = null;
        SetActiveError = null;
    }

    private void CloseAllOverlays()
    {
        CloseCreateEmployee();
        CloseResetPassword();
        CloseSetActiveConfirmation();
    }

    private void PopulatePermissionRows()
    {
        foreach (var row in PermissionRows)
        {
            var permission = SelectedUser?.Permissions.FirstOrDefault(item => item.Module == row.Module);
            row.Load(permission);
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
        _currentUserSession.IsAuthenticated && _currentUserSession.Role == UserRole.Responsable;

    private bool IsSameSession(SessionIdentity identity) =>
        IsResponsibleSession()
        && _currentUserSession.UserId == identity.UserId
        && string.Equals(_currentUserSession.Username, identity.Username, StringComparison.Ordinal);

    private static bool IsAuthenticationFailure(UserAdministrationFailureReason reason) =>
        reason is UserAdministrationFailureReason.NotAuthenticated or UserAdministrationFailureReason.Forbidden;

    private static string MapFailure(UserAdministrationFailureReason reason, string fallback) => reason switch
    {
        UserAdministrationFailureReason.InvalidFullName => "Nom complet invalide.",
        UserAdministrationFailureReason.InvalidUsername => "Nom d'utilisateur invalide.",
        UserAdministrationFailureReason.InvalidPassword => "Mot de passe invalide.",
        UserAdministrationFailureReason.DuplicateUsername => "Ce nom d'utilisateur existe déjà.",
        UserAdministrationFailureReason.UserNotFound => "L'utilisateur n'existe plus.",
        UserAdministrationFailureReason.InvalidModule => "Module de permission invalide.",
        UserAdministrationFailureReason.EmployeeRequired => "Les permissions configurables concernent uniquement les employés.",
        UserAdministrationFailureReason.LastActiveResponsible => "Le dernier Responsable actif ne peut pas être désactivé.",
        UserAdministrationFailureReason.ConcurrentOperation => "Une autre modification est en cours. Réessayez.",
        _ => fallback
    };

    private void FailClosed()
    {
        CloseAllOverlays();
        ClearProtectedData();
        ErrorMessage = "Votre session n'autorise plus l'administration.";
        AuthenticationInvalidated?.Invoke(this, EventArgs.Empty);
    }

    private void ClearProtectedData()
    {
        Users.Clear();
        SelectedUser = null;
        _hasLoaded = false;
        SuccessMessage = null;
    }

    private void NotifyBusyChanged()
    {
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CanOperate));
        RaiseCommandCanExecuteChanged();
    }

    private void RaiseCommandCanExecuteChanged()
    {
        _refreshRelayCommand.RaiseCanExecuteChanged();
        _showCreateEmployeeRelayCommand.RaiseCanExecuteChanged();
        _cancelCreateEmployeeRelayCommand.RaiseCanExecuteChanged();
        _createEmployeeRelayCommand.RaiseCanExecuteChanged();
        _savePermissionsRelayCommand.RaiseCanExecuteChanged();
        _requestSetActiveRelayCommand.RaiseCanExecuteChanged();
        _cancelSetActiveRelayCommand.RaiseCanExecuteChanged();
        _confirmSetActiveRelayCommand.RaiseCanExecuteChanged();
        _showResetPasswordRelayCommand.RaiseCanExecuteChanged();
        _cancelResetPasswordRelayCommand.RaiseCanExecuteChanged();
        _resetPasswordRelayCommand.RaiseCanExecuteChanged();
    }

    private Task Track(Task operation)
    {
        _pendingOperation = operation;
        return operation;
    }

    private void Child_AuthenticationInvalidated(object? sender, EventArgs e) => FailClosed();

    private readonly record struct SessionIdentity(long UserId, string? Username);
}
