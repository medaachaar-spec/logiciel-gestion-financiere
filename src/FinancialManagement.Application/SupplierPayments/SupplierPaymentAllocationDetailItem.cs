namespace FinancialManagement.Application.SupplierPayments;

public sealed record SupplierPaymentAllocationDetailItem(
    long Id,
    SupplierPaymentAllocationTargetType TargetType,
    long TargetId,
    string TargetReference,
    long OriginalObligationAmountMinor,
    long AllocatedAmountMinor);
