namespace FinancialManagement.Application.Purchases;

public sealed class CreatePurchaseResult
{
    private CreatePurchaseResult(
        bool success,
        CreatePurchaseFailureReason failureReason,
        long? purchaseId,
        string? purchaseReference,
        long? supplierPaymentId,
        string? supplierPaymentReference,
        long totalAmountMinor,
        long allocatedAmountMinor,
        long remainingPayableMinor,
        long unallocatedSupplierAdvanceMinor)
    {
        Success = success;
        FailureReason = failureReason;
        PurchaseId = purchaseId;
        PurchaseReference = purchaseReference;
        SupplierPaymentId = supplierPaymentId;
        SupplierPaymentReference = supplierPaymentReference;
        TotalAmountMinor = totalAmountMinor;
        AllocatedAmountMinor = allocatedAmountMinor;
        RemainingPayableMinor = remainingPayableMinor;
        UnallocatedSupplierAdvanceMinor = unallocatedSupplierAdvanceMinor;
    }

    public bool Success { get; }

    public CreatePurchaseFailureReason FailureReason { get; }

    public long? PurchaseId { get; }

    public string? PurchaseReference { get; }

    public long? SupplierPaymentId { get; }

    public string? SupplierPaymentReference { get; }

    public long TotalAmountMinor { get; }

    public long AllocatedAmountMinor { get; }

    public long RemainingPayableMinor { get; }

    public long UnallocatedSupplierAdvanceMinor { get; }

    public static CreatePurchaseResult Failed(
        CreatePurchaseFailureReason failureReason,
        long totalAmountMinor) =>
        new(
            success: false,
            failureReason,
            purchaseId: null,
            purchaseReference: null,
            supplierPaymentId: null,
            supplierPaymentReference: null,
            totalAmountMinor,
            allocatedAmountMinor: 0,
            remainingPayableMinor: 0,
            unallocatedSupplierAdvanceMinor: 0);

    public static CreatePurchaseResult Succeeded(
        long purchaseId,
        string purchaseReference,
        long? supplierPaymentId,
        string? supplierPaymentReference,
        long totalAmountMinor,
        long allocatedAmountMinor,
        long remainingPayableMinor,
        long unallocatedSupplierAdvanceMinor) =>
        new(
            success: true,
            CreatePurchaseFailureReason.None,
            purchaseId,
            purchaseReference,
            supplierPaymentId,
            supplierPaymentReference,
            totalAmountMinor,
            allocatedAmountMinor,
            remainingPayableMinor,
            unallocatedSupplierAdvanceMinor);
}
