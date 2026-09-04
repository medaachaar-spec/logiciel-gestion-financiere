namespace FinancialManagement.Application.SupplierPayments;

public interface ISupplierPaymentService
{
    Task<CreateSupplierPaymentResult> CreateAsync(
        CreateSupplierPaymentCommand command,
        CancellationToken cancellationToken = default);
}
