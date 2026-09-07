namespace FinancialManagement.Application.SupplierPayments;

public interface ISupplierPaymentService
{
    Task<CreateSupplierPaymentResult> CreateAsync(
        CreateSupplierPaymentCommand command,
        CancellationToken cancellationToken = default);

    Task<SupplierPaymentListResult> GetPageAsync(
        SupplierPaymentListQuery query,
        CancellationToken cancellationToken = default);

    Task<SupplierPaymentDetailResult> GetByIdAsync(
        long supplierPaymentId,
        CancellationToken cancellationToken = default);
}
