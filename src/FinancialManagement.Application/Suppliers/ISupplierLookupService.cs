namespace FinancialManagement.Application.Suppliers;

public interface ISupplierLookupService
{
    Task<SupplierLookupResult> SearchAsync(
        SupplierLookupQuery query,
        CancellationToken cancellationToken = default);
}
