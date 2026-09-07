namespace FinancialManagement.Application.CustomerPayments;

public sealed class CustomerPaymentDetailResult
{
    private CustomerPaymentDetailResult(
        bool success,
        CustomerPaymentDetailFailureReason failureReason,
        CustomerPaymentDetailItem? item)
    {
        Success = success;
        FailureReason = failureReason;
        Item = item;
    }

    public bool Success { get; }

    public CustomerPaymentDetailFailureReason FailureReason { get; }

    public CustomerPaymentDetailItem? Item { get; }

    public static CustomerPaymentDetailResult Failed(
        CustomerPaymentDetailFailureReason failureReason) =>
        new(false, failureReason, null);

    public static CustomerPaymentDetailResult Succeeded(CustomerPaymentDetailItem item) =>
        new(true, CustomerPaymentDetailFailureReason.None, item);
}
