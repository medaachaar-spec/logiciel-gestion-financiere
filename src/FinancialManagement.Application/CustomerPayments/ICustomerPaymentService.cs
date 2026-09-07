namespace FinancialManagement.Application.CustomerPayments;

public interface ICustomerPaymentService
{
    Task<CreateCustomerPaymentResult> CreateAsync(
        CreateCustomerPaymentCommand command,
        CancellationToken cancellationToken = default);

    Task<CustomerPaymentListResult> GetPageAsync(
        CustomerPaymentListQuery query,
        CancellationToken cancellationToken = default);

    Task<CustomerPaymentDetailResult> GetByIdAsync(
        long customerPaymentId,
        CancellationToken cancellationToken = default);
}
