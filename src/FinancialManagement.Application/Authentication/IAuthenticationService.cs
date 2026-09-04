namespace FinancialManagement.Application.Authentication;

public interface IAuthenticationService
{
    Task<AuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default);

    void Logout();
}
