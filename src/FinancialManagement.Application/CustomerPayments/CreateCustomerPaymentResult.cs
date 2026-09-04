namespace FinancialManagement.Application.CustomerPayments;

public sealed class CreateCustomerPaymentResult
{
    private CreateCustomerPaymentResult(
        bool success,
        CreateCustomerPaymentFailureReason failureReason,
        long? customerPaymentId,
        string? customerPaymentReference,
        long amountMinor,
        long allocatedAmountMinor,
        long unallocatedAdvanceMinor,
        int allocationCount)
    {
        Success = success;
        FailureReason = failureReason;
        CustomerPaymentId = customerPaymentId;
        CustomerPaymentReference = customerPaymentReference;
        AmountMinor = amountMinor;
        AllocatedAmountMinor = allocatedAmountMinor;
        UnallocatedAdvanceMinor = unallocatedAdvanceMinor;
        AllocationCount = allocationCount;
    }

    public bool Success { get; }

    public CreateCustomerPaymentFailureReason FailureReason { get; }

    public long? CustomerPaymentId { get; }

    public string? CustomerPaymentReference { get; }

    public long AmountMinor { get; }

    public long AllocatedAmountMinor { get; }

    public long UnallocatedAdvanceMinor { get; }

    public int AllocationCount { get; }

    public static CreateCustomerPaymentResult Failed(
        CreateCustomerPaymentFailureReason failureReason,
        long amountMinor) =>
        new(
            success: false,
            failureReason,
            customerPaymentId: null,
            customerPaymentReference: null,
            amountMinor,
            allocatedAmountMinor: 0,
            unallocatedAdvanceMinor: 0,
            allocationCount: 0);

    public static CreateCustomerPaymentResult Succeeded(
        long customerPaymentId,
        string customerPaymentReference,
        long amountMinor,
        long allocatedAmountMinor,
        long unallocatedAdvanceMinor,
        int allocationCount) =>
        new(
            success: true,
            CreateCustomerPaymentFailureReason.None,
            customerPaymentId,
            customerPaymentReference,
            amountMinor,
            allocatedAmountMinor,
            unallocatedAdvanceMinor,
            allocationCount);
}
