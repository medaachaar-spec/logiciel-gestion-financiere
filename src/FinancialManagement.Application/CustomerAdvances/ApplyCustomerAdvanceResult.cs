namespace FinancialManagement.Application.CustomerAdvances;

public sealed class ApplyCustomerAdvanceResult
{
    private ApplyCustomerAdvanceResult(
        bool success,
        ApplyCustomerAdvanceFailureReason failureReason,
        long? customerPaymentId,
        string? customerPaymentReference,
        long previousAvailableAdvanceMinor,
        long appliedAmountMinor,
        long remainingAvailableAdvanceMinor,
        int allocationCount)
    {
        Success = success;
        FailureReason = failureReason;
        CustomerPaymentId = customerPaymentId;
        CustomerPaymentReference = customerPaymentReference;
        PreviousAvailableAdvanceMinor = previousAvailableAdvanceMinor;
        AppliedAmountMinor = appliedAmountMinor;
        RemainingAvailableAdvanceMinor = remainingAvailableAdvanceMinor;
        AllocationCount = allocationCount;
    }

    public bool Success { get; }

    public ApplyCustomerAdvanceFailureReason FailureReason { get; }

    public long? CustomerPaymentId { get; }

    public string? CustomerPaymentReference { get; }

    public long PreviousAvailableAdvanceMinor { get; }

    public long AppliedAmountMinor { get; }

    public long RemainingAvailableAdvanceMinor { get; }

    public int AllocationCount { get; }

    public static ApplyCustomerAdvanceResult Failed(
        ApplyCustomerAdvanceFailureReason failureReason) =>
        new(false, failureReason, null, null, 0, 0, 0, 0);

    public static ApplyCustomerAdvanceResult Succeeded(
        long customerPaymentId,
        string customerPaymentReference,
        long previousAvailableAdvanceMinor,
        long appliedAmountMinor,
        long remainingAvailableAdvanceMinor,
        int allocationCount) =>
        new(
            true,
            ApplyCustomerAdvanceFailureReason.None,
            customerPaymentId,
            customerPaymentReference,
            previousAvailableAdvanceMinor,
            appliedAmountMinor,
            remainingAvailableAdvanceMinor,
            allocationCount);
}
