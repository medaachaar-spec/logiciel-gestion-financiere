namespace FinancialManagement.Application.SupplierAdvances;

public sealed record SupplierAdvanceAllocationInput(
    long? PurchaseId,
    long? InitialSupplierPayableId,
    long AmountMinor);
