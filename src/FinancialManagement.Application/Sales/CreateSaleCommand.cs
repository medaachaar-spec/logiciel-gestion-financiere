namespace FinancialManagement.Application.Sales;

public sealed record CreateSaleCommand(
    long? ClientId,
    long TotalAmountMinor,
    string? Observation,
    DateOnly? DueDate,
    InitialCustomerPaymentInput? InitialPayment);
