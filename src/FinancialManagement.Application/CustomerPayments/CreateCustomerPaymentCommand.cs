using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.CustomerPayments;

public sealed record CreateCustomerPaymentCommand(
    long ClientId,
    long AmountMinor,
    PaymentMethod PaymentMethod,
    string? Observation,
    IReadOnlyCollection<CustomerPaymentAllocationInput>? Allocations);
