using FinancialManagement.Application.Authentication;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Infrastructure.Authentication;

internal sealed class CurrentUserSession : ICurrentUserSession
{
    public bool IsAuthenticated => UserId.HasValue;

    public long? UserId { get; private set; }

    public string? FullName { get; private set; }

    public string? Username { get; private set; }

    public UserRole? Role { get; private set; }

    public void SetUser(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);

        UserId = user.UserId;
        FullName = user.FullName;
        Username = user.Username;
        Role = user.Role;
    }

    public void Clear()
    {
        UserId = null;
        FullName = null;
        Username = null;
        Role = null;
    }
}
