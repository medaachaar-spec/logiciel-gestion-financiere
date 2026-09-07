using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.SupplierPayments;

public sealed record SupplierPaymentListItem(
    long Id,
    string Reference,
    DateOnly PaymentDate,
    long SupplierId,
    string SupplierName,
    long AmountMinor,
    PaymentMethod PaymentMethod,
    long AllocatedAmountMinor,
    long UnallocatedAmountMinor,
    string? Observation);
