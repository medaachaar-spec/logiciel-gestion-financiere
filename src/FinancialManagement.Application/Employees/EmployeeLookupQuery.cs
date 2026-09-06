namespace FinancialManagement.Application.Employees;

public sealed record EmployeeLookupQuery(
    string? SearchText = null,
    int Limit = 20);
