using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.Sales;

public sealed record InitialCustomerPaymentInput(
    long AmountMinor,
    PaymentMethod PaymentMethod,
    string? Observation);
