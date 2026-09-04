namespace FinancialManagement.Application.SupplierPayments;

public sealed class CreateSupplierPaymentResult
{
    private CreateSupplierPaymentResult(
        bool success,
        CreateSupplierPaymentFailureReason failureReason,
        long? supplierPaymentId,
        string? supplierPaymentReference,
        long amountMinor,
        long allocatedAmountMinor,
        long unallocatedSupplierAdvanceMinor,
        int allocationCount)
    {
        Success = success;
        FailureReason = failureReason;
        SupplierPaymentId = supplierPaymentId;
        SupplierPaymentReference = supplierPaymentReference;
        AmountMinor = amountMinor;
        AllocatedAmountMinor = allocatedAmountMinor;
        UnallocatedSupplierAdvanceMinor = unallocatedSupplierAdvanceMinor;
        AllocationCount = allocationCount;
    }

    public bool Success { get; }

    public CreateSupplierPaymentFailureReason FailureReason { get; }

    public long? SupplierPaymentId { get; }

    public string? SupplierPaymentReference { get; }

    public long AmountMinor { get; }

    public long AllocatedAmountMinor { get; }

    public long UnallocatedSupplierAdvanceMinor { get; }

    public int AllocationCount { get; }

    public static CreateSupplierPaymentResult Failed(
        CreateSupplierPaymentFailureReason failureReason,
        long amountMinor) =>
        new(
            success: false,
            failureReason,
            supplierPaymentId: null,
            supplierPaymentReference: null,
            amountMinor,
            allocatedAmountMinor: 0,
            unallocatedSupplierAdvanceMinor: 0,
            allocationCount: 0);

    public static CreateSupplierPaymentResult Succeeded(
        long supplierPaymentId,
        string supplierPaymentReference,
        long amountMinor,
        long allocatedAmountMinor,
        long unallocatedSupplierAdvanceMinor,
        int allocationCount) =>
        new(
            success: true,
            CreateSupplierPaymentFailureReason.None,
            supplierPaymentId,
            supplierPaymentReference,
            amountMinor,
            allocatedAmountMinor,
            unallocatedSupplierAdvanceMinor,
            allocationCount);
}
