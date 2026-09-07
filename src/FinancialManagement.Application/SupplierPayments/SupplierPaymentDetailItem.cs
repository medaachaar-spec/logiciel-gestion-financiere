using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.SupplierPayments;

public sealed record SupplierPaymentDetailItem(
    long Id,
    string Reference,
    DateOnly PaymentDate,
    long SupplierId,
    string SupplierName,
    long AmountMinor,
    PaymentMethod PaymentMethod,
    long AllocatedAmountMinor,
    long UnallocatedAmountMinor,
    string? Observation,
    IReadOnlyList<SupplierPaymentAllocationDetailItem> Allocations);
