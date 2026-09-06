namespace FinancialManagement.Application.Suppliers;

public interface ISupplierService
{
    Task<CreateSupplierResult> CreateAsync(
        CreateSupplierCommand command,
        CancellationToken cancellationToken = default);

    Task<SupplierListResult> GetPageAsync(
        SupplierListQuery query,
        CancellationToken cancellationToken = default);

    Task<SupplierDetailResult> GetByIdAsync(
        long supplierId,
        CancellationToken cancellationToken = default);

    Task<UpdateSupplierResult> UpdateAsync(
        UpdateSupplierCommand command,
        CancellationToken cancellationToken = default);

    Task<DeleteSupplierResult> DeleteAsync(
        long supplierId,
        CancellationToken cancellationToken = default);
}
