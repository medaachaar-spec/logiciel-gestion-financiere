namespace FinancialManagement.Application.Authentication;

public sealed class AuthenticationResult
{
    private AuthenticationResult(bool success, AuthenticatedUser? user)
    {
        Success = success;
        User = user;
    }

    public bool Success { get; }

    public AuthenticatedUser? User { get; }

    public static AuthenticationResult Succeeded(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        return new AuthenticationResult(true, user);
    }

    public static AuthenticationResult Failed() => new(false, null);
}
