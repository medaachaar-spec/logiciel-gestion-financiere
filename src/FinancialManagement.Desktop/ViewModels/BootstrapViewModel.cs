using System.Diagnostics;
using System.Windows.Input;
using FinancialManagement.Application.Initialization;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class BootstrapViewModel : ViewModelBase
{
    private readonly IApplicationInitializationService _initializationService;
    private readonly AsyncRelayCommand _submitRelayCommand;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private string _fullName = string.Empty;
    private string _username = string.Empty;
    private string? _errorMessage;
    private string? _successMessage;
    private bool _isBusy;
    private bool _isStopped;
    private Task _pendingOperation = Task.CompletedTask;

    public BootstrapViewModel(IApplicationInitializationService initializationService)
    {
        ArgumentNullException.ThrowIfNull(initializationService);
        _initializationService = initializationService;
        _submitRelayCommand = new AsyncRelayCommand(
            parameter => Track(SubmitAsync(parameter as CredentialSubmission)),
            _ => !IsBusy && !_isStopped);
        SubmitCommand = _submitRelayCommand;
    }

    public event EventHandler? ReadyForLogin;

    public ICommand SubmitCommand { get; }

    public string FullName
    {
        get => _fullName;
        set
        {
            if (SetProperty(ref _fullName, value))
            {
                ClearFeedback();
            }
        }
    }

    public string Username
    {
        get => _username;
        set
        {
            if (SetProperty(ref _username, value))
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
                OnPropertyChanged(nameof(SubmitButtonText));
                _submitRelayCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SubmitButtonText => IsBusy ? "Création en cours…" : "Créer le Responsable";

    public void Stop()
    {
        if (_isStopped)
        {
            return;
        }

        _isStopped = true;
        _lifetimeCancellation.Cancel();
        _submitRelayCommand.RaiseCanExecuteChanged();
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

    private async Task SubmitAsync(CredentialSubmission? credentials)
    {
        if (credentials is null || IsBusy || _isStopped)
        {
            return;
        }

        ClearFeedback();
        var fullName = FullName.Trim();
        var username = Username.Trim();
        if (string.IsNullOrWhiteSpace(fullName))
        {
            ErrorMessage = "Veuillez renseigner le nom complet.";
            return;
        }

        if (string.IsNullOrWhiteSpace(username))
        {
            ErrorMessage = "Veuillez renseigner le nom d'utilisateur.";
            return;
        }

        if (string.IsNullOrEmpty(credentials.Password))
        {
            ErrorMessage = "Veuillez renseigner le mot de passe.";
            return;
        }

        if (!string.Equals(credentials.Password, credentials.Confirmation, StringComparison.Ordinal))
        {
            ErrorMessage = "Les mots de passe ne correspondent pas.";
            return;
        }

        IsBusy = true;
        try
        {
            var result = await _initializationService.BootstrapResponsibleAsync(
                new BootstrapResponsibleCommand(fullName, username, credentials.Password),
                _lifetimeCancellation.Token);

            if (result.Success)
            {
                SuccessMessage = "Le compte Responsable a été créé. Connectez-vous pour terminer l'initialisation.";
                ReadyForLogin?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (result.FailureReason is BootstrapResponsibleFailureReason.BootstrapUnavailable
                or BootstrapResponsibleFailureReason.ConcurrentOperation)
            {
                var state = await _initializationService.GetStateAsync(_lifetimeCancellation.Token);
                if (!state.RequiresBootstrap)
                {
                    SuccessMessage = "L'application a déjà été initialisée. Connectez-vous avec le compte Responsable.";
                    ReadyForLogin?.Invoke(this, EventArgs.Empty);
                    return;
                }
            }

            ErrorMessage = result.FailureReason switch
            {
                BootstrapResponsibleFailureReason.BootstrapUnavailable =>
                    "La création du premier Responsable n'est plus disponible. Rechargez l'application.",
                BootstrapResponsibleFailureReason.InvalidFullName => "Nom complet invalide.",
                BootstrapResponsibleFailureReason.InvalidUsername => "Nom d'utilisateur invalide.",
                BootstrapResponsibleFailureReason.InvalidPassword => "Mot de passe invalide.",
                BootstrapResponsibleFailureReason.ConcurrentOperation =>
                    "Une autre initialisation est en cours. Réessayez dans un instant.",
                _ => "Impossible de créer le compte Responsable."
            };
        }
        catch (OperationCanceledException) when (_lifetimeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Trace.TraceError("Responsible bootstrap failed unexpectedly: {0}", exception);
            ErrorMessage = "Une erreur est survenue pendant l'initialisation. Veuillez réessayer.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ClearFeedback()
    {
        ErrorMessage = null;
        SuccessMessage = null;
    }

    private Task Track(Task operation)
    {
        _pendingOperation = operation;
        return operation;
    }
}
