using System.Diagnostics;
using FinancialManagement.Application.Authentication;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class LoginViewModel : ViewModelBase
{
    private const string InvalidCredentialsMessage =
        "Nom d'utilisateur ou mot de passe incorrect.";
    private const string MissingCredentialsMessage =
        "Veuillez renseigner le nom d'utilisateur et le mot de passe.";
    private const string TechnicalFailureMessage =
        "Une erreur est survenue. Veuillez réessayer.";

    private readonly IAuthenticationService _authenticationService;
    private readonly ICurrentUserSession _currentUserSession;
    private string _username = string.Empty;
    private string _errorMessage = string.Empty;
    private bool _isBusy;

    public LoginViewModel(
        IAuthenticationService authenticationService,
        ICurrentUserSession currentUserSession)
    {
        _authenticationService = authenticationService;
        _currentUserSession = currentUserSession;
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string ErrorMessage
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

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanSubmit));
                OnPropertyChanged(nameof(SubmitButtonText));
            }
        }
    }

    public bool CanSubmit => !IsBusy;

    public string SubmitButtonText => IsBusy ? "Connexion…" : "Se connecter";

    public async Task<bool> AuthenticateAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        if (IsBusy)
        {
            return false;
        }

        ErrorMessage = string.Empty;
        var username = Username.Trim();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            ErrorMessage = MissingCredentialsMessage;
            return false;
        }

        IsBusy = true;

        try
        {
            var result = await _authenticationService.AuthenticateAsync(
                username,
                password,
                cancellationToken);

            if (!result.Success)
            {
                ErrorMessage = InvalidCredentialsMessage;
                return false;
            }

            if (result.User is null
                || !_currentUserSession.IsAuthenticated
                || _currentUserSession.UserId != result.User.UserId)
            {
                ErrorMessage = TechnicalFailureMessage;
                return false;
            }

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ErrorMessage = TechnicalFailureMessage;
            return false;
        }
        catch (Exception exception)
        {
            Trace.TraceError("Authentication failed unexpectedly: {0}", exception);
            ErrorMessage = TechnicalFailureMessage;
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
