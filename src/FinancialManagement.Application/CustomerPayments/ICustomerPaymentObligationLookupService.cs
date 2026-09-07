namespace FinancialManagement.Application.CustomerPayments;

public interface ICustomerPaymentObligationLookupService
{
    Task<CustomerPaymentObligationLookupResult> SearchAsync(
        CustomerPaymentObligationLookupQuery query,
        CancellationToken cancellationToken = default);
}
