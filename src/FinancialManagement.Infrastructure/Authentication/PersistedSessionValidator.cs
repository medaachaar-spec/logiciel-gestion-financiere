using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Authentication;

internal interface IPersistedSessionValidator
{
    Task<bool> IsCurrentSessionValidAsync(CancellationToken cancellationToken = default);
}

internal sealed class PersistedSessionValidator(
    FinancialManagementDbContext dbContext,
    CurrentUserSession currentUserSession) : IPersistedSessionValidator
{
    public async Task<bool> IsCurrentSessionValidAsync(CancellationToken cancellationToken = default)
    {
        var sessionState = currentUserSession.CaptureSecurityState();
        if (sessionState is null)
        {
            return false;
        }

        var persisted = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == sessionState.UserId)
            .Select(user => new
            {
                user.Id,
                user.Username,
                user.Role,
                user.IsActive,
                user.PasswordHash,
                user.UpdatedAtUtc
            })
            .SingleOrDefaultAsync(cancellationToken);

        var isValid = persisted is not null
            && persisted.IsActive
            && persisted.Role == sessionState.Role
            && string.Equals(
                SessionSecurityMarker.Create(
                    persisted.Id,
                    persisted.Username,
                    persisted.Role,
                    persisted.IsActive,
                    persisted.PasswordHash,
                    persisted.UpdatedAtUtc),
                sessionState.SecurityMarker,
                StringComparison.Ordinal)
            && currentUserSession.Matches(sessionState);

        if (!isValid)
        {
            currentUserSession.ClearIfMatches(sessionState);
        }

        return isValid;
    }
}
