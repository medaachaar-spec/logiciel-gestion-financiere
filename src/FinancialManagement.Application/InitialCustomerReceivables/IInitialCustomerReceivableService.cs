namespace FinancialManagement.Application.InitialCustomerReceivables;

public interface IInitialCustomerReceivableService
{
    Task<CreateInitialCustomerReceivableResult> CreateAsync(
        CreateInitialCustomerReceivableCommand command,
        CancellationToken cancellationToken = default);
}
