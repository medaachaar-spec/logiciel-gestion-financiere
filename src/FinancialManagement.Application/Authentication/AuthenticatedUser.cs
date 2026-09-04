using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.Authentication;

public sealed record AuthenticatedUser(
    long UserId,
    string FullName,
    string Username,
    UserRole Role);
