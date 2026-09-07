using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.Administration;

public sealed record EmployeePermissionInput(PermissionModule Module, bool CanView, bool CanCreate, bool CanModify);
