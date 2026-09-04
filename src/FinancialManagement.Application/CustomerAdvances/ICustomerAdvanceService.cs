namespace FinancialManagement.Application.CustomerAdvances;

public interface ICustomerAdvanceService
{
    Task<ApplyCustomerAdvanceResult> ApplyAsync(
        ApplyCustomerAdvanceCommand command,
        CancellationToken cancellationToken = default);
}
