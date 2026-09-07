namespace FinancialManagement.Application.SupplierPayments;

public sealed class SupplierPaymentDetailResult
{
    private SupplierPaymentDetailResult(
        bool success,
        SupplierPaymentDetailFailureReason failureReason,
        SupplierPaymentDetailItem? item)
    {
        Success = success;
        FailureReason = failureReason;
        Item = item;
    }

    public bool Success { get; }

    public SupplierPaymentDetailFailureReason FailureReason { get; }

    public SupplierPaymentDetailItem? Item { get; }

    public static SupplierPaymentDetailResult Failed(
        SupplierPaymentDetailFailureReason failureReason) =>
        new(false, failureReason, null);

    public static SupplierPaymentDetailResult Succeeded(SupplierPaymentDetailItem item) =>
        new(true, SupplierPaymentDetailFailureReason.None, item);
}
