using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.Administration;

public sealed record UserAdministrationItem(
    long Id, string FullName, string Username, UserRole Role, bool IsActive,
    IReadOnlyList<EmployeePermissionInput> Permissions);
