using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.SupplierPayments;

public sealed record CreateSupplierPaymentCommand(
    long SupplierId,
    long AmountMinor,
    PaymentMethod PaymentMethod,
    string? Observation,
    IReadOnlyCollection<SupplierPaymentAllocationInput>? Allocations);
