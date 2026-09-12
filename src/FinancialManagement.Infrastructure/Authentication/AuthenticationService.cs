using FinancialManagement.Application.Authentication;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Authentication;

internal sealed class AuthenticationService(
    FinancialManagementDbContext dbContext,
    IPasswordHasher passwordHasher,
    CurrentUserSession currentUserSession) : IAuthenticationService
{
    public async Task<AuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        currentUserSession.Clear();

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            return AuthenticationResult.Failed();
        }

        var user = await dbContext.Users
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.Username == username, cancellationToken);

        if (user is null || !user.IsActive)
        {
            return AuthenticationResult.Failed();
        }

        if (!passwordHasher.VerifyPassword(user.PasswordHash, password))
        {
            return AuthenticationResult.Failed();
        }

        var authenticatedUser = new AuthenticatedUser(
            user.Id,
            user.FullName,
            user.Username,
            user.Role);

        currentUserSession.SetUser(
            authenticatedUser,
            SessionSecurityMarker.Create(
                user.Id,
                user.Username,
                user.Role,
                user.IsActive,
                user.PasswordHash,
                user.UpdatedAtUtc));
        return AuthenticationResult.Succeeded(authenticatedUser);
    }

    public void Logout() => currentUserSession.Clear();
}
