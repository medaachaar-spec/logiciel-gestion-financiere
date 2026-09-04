using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.Purchases;

public sealed record InitialSupplierPaymentInput(
    long AmountMinor,
    PaymentMethod PaymentMethod,
    string? Observation);
