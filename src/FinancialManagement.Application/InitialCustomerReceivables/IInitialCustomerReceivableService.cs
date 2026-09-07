namespace FinancialManagement.Application.InitialCustomerReceivables;

public interface IInitialCustomerReceivableService
{
    Task<InitialCustomerReceivableListResult> GetPageAsync(
        InitialCustomerReceivableListQuery query, CancellationToken cancellationToken = default);

    Task<CreateInitialCustomerReceivableResult> CreateAsync(
        CreateInitialCustomerReceivableCommand command,
        CancellationToken cancellationToken = default);
}
