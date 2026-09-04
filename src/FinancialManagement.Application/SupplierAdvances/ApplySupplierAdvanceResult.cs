namespace FinancialManagement.Application.SupplierAdvances;

public sealed class ApplySupplierAdvanceResult
{
    private ApplySupplierAdvanceResult(
        bool success,
        ApplySupplierAdvanceFailureReason failureReason,
        long? supplierPaymentId,
        string? supplierPaymentReference,
        long previousAvailableAdvanceMinor,
        long appliedAmountMinor,
        long remainingAvailableAdvanceMinor,
        int allocationCount)
    {
        Success = success;
        FailureReason = failureReason;
        SupplierPaymentId = supplierPaymentId;
        SupplierPaymentReference = supplierPaymentReference;
        PreviousAvailableAdvanceMinor = previousAvailableAdvanceMinor;
        AppliedAmountMinor = appliedAmountMinor;
        RemainingAvailableAdvanceMinor = remainingAvailableAdvanceMinor;
        AllocationCount = allocationCount;
    }

    public bool Success { get; }

    public ApplySupplierAdvanceFailureReason FailureReason { get; }

    public long? SupplierPaymentId { get; }

    public string? SupplierPaymentReference { get; }

    public long PreviousAvailableAdvanceMinor { get; }

    public long AppliedAmountMinor { get; }

    public long RemainingAvailableAdvanceMinor { get; }

    public int AllocationCount { get; }

    public static ApplySupplierAdvanceResult Failed(
        ApplySupplierAdvanceFailureReason failureReason) =>
        new(false, failureReason, null, null, 0, 0, 0, 0);

    public static ApplySupplierAdvanceResult Succeeded(
        long supplierPaymentId,
        string supplierPaymentReference,
        long previousAvailableAdvanceMinor,
        long appliedAmountMinor,
        long remainingAvailableAdvanceMinor,
        int allocationCount) =>
        new(
            true,
            ApplySupplierAdvanceFailureReason.None,
            supplierPaymentId,
            supplierPaymentReference,
            previousAvailableAdvanceMinor,
            appliedAmountMinor,
            remainingAvailableAdvanceMinor,
            allocationCount);
}
