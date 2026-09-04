namespace FinancialManagement.Application.InitialSupplierPayables;

public interface IInitialSupplierPayableService
{
    Task<CreateInitialSupplierPayableResult> CreateAsync(
        CreateInitialSupplierPayableCommand command,
        CancellationToken cancellationToken = default);
}
