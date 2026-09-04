namespace FinancialManagement.Application.SupplierPayments;

public sealed record SupplierPaymentAllocationInput(
    long? PurchaseId,
    long? InitialSupplierPayableId,
    long AmountMinor);
