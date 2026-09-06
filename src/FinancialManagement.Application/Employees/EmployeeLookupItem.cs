namespace FinancialManagement.Application.Employees;

public sealed record EmployeeLookupItem(
    long UserId,
    string DisplayName,
    string Username);
