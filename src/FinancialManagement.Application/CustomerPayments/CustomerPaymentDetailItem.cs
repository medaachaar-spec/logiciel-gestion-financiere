using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.CustomerPayments;

public sealed record CustomerPaymentDetailItem(
    long Id,
    string Reference,
    DateOnly PaymentDate,
    long? ClientId,
    string? ClientName,
    long AmountMinor,
    PaymentMethod PaymentMethod,
    long AllocatedAmountMinor,
    long UnallocatedAmountMinor,
    string? Observation,
    IReadOnlyList<CustomerPaymentAllocationDetailItem> Allocations);
