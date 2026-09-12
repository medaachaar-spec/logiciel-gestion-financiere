using FinancialManagement.Application.Authentication;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Infrastructure.Authentication;

internal sealed class CurrentUserSession : ICurrentUserSession
{
    private long _version;
    private string? _securityMarker;

    public bool IsAuthenticated => UserId.HasValue;

    public long? UserId { get; private set; }

    public string? FullName { get; private set; }

    public string? Username { get; private set; }

    public UserRole? Role { get; private set; }

    public void SetUser(AuthenticatedUser user, string securityMarker)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(securityMarker);

        UserId = user.UserId;
        FullName = user.FullName;
        Username = user.Username;
        Role = user.Role;
        _securityMarker = securityMarker;
        _version++;
    }

    internal void SetUser(AuthenticatedUser user) =>
        SetUser(user, "unvalidated-session");

    public void Clear()
    {
        UserId = null;
        FullName = null;
        Username = null;
        Role = null;
        _securityMarker = null;
        _version++;
    }

    internal SessionSecurityState? CaptureSecurityState() =>
        UserId is long userId
        && Role is UserRole role
        && _securityMarker is not null
            ? new SessionSecurityState(userId, role, _securityMarker, _version)
            : null;

    internal bool Matches(SessionSecurityState state) =>
        UserId == state.UserId
        && Role == state.Role
        && _version == state.Version
        && string.Equals(_securityMarker, state.SecurityMarker, StringComparison.Ordinal);

    internal void ClearIfMatches(SessionSecurityState state)
    {
        if (Matches(state))
        {
            Clear();
        }
    }
}

internal sealed record SessionSecurityState(
    long UserId,
    UserRole Role,
    string SecurityMarker,
    long Version);
