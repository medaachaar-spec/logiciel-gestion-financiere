namespace FinancialManagement.Application.CustomerPayments;

public interface ICustomerPaymentService
{
    Task<CreateCustomerPaymentResult> CreateAsync(
        CreateCustomerPaymentCommand command,
        CancellationToken cancellationToken = default);
}
