namespace FinancialManagement.Application.Employees;

public interface IEmployeeLookupService
{
    Task<EmployeeLookupResult> SearchAsync(
        EmployeeLookupQuery query,
        CancellationToken cancellationToken = default);
}
