namespace FinancialManagement.Application.InitialSupplierPayables;

public interface IInitialSupplierPayableService
{
    Task<InitialSupplierPayableListResult> GetPageAsync(
        InitialSupplierPayableListQuery query, CancellationToken cancellationToken = default);

    Task<CreateInitialSupplierPayableResult> CreateAsync(
        CreateInitialSupplierPayableCommand command,
        CancellationToken cancellationToken = default);
}
