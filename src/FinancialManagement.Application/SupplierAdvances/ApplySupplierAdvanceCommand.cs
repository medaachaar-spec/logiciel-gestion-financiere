namespace FinancialManagement.Application.SupplierAdvances;

public sealed record ApplySupplierAdvanceCommand(
    long SupplierPaymentId,
    IReadOnlyCollection<SupplierAdvanceAllocationInput>? Allocations);
