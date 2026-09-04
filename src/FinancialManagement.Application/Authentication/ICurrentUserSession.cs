using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.Authentication;

public interface ICurrentUserSession
{
    bool IsAuthenticated { get; }

    long? UserId { get; }

    string? FullName { get; }

    string? Username { get; }

    UserRole? Role { get; }
}
