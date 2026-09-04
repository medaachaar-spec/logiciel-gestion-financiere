namespace FinancialManagement.Application.CustomerPayments;

public sealed record CustomerPaymentAllocationInput(
    long? SaleId,
    long? InitialCustomerReceivableId,
    long AmountMinor);
