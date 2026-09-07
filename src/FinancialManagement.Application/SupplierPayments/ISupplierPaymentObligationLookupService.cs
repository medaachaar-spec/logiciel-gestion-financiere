namespace FinancialManagement.Application.SupplierPayments;

public interface ISupplierPaymentObligationLookupService
{
    Task<SupplierPaymentObligationLookupResult> SearchAsync(
        SupplierPaymentObligationLookupQuery query,
        CancellationToken cancellationToken = default);
}
