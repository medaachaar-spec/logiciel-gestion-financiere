namespace FinancialManagement.Application.Sales;

public sealed class CreateSaleResult
{
    private CreateSaleResult(
        bool success,
        CreateSaleFailureReason failureReason,
        long? saleId,
        string? saleReference,
        long? customerPaymentId,
        string? customerPaymentReference,
        long totalAmountMinor,
        long allocatedAmountMinor,
        long remainingReceivableMinor,
        long unallocatedAdvanceMinor)
    {
        Success = success;
        FailureReason = failureReason;
        SaleId = saleId;
        SaleReference = saleReference;
        CustomerPaymentId = customerPaymentId;
        CustomerPaymentReference = customerPaymentReference;
        TotalAmountMinor = totalAmountMinor;
        AllocatedAmountMinor = allocatedAmountMinor;
        RemainingReceivableMinor = remainingReceivableMinor;
        UnallocatedAdvanceMinor = unallocatedAdvanceMinor;
    }

    public bool Success { get; }

    public CreateSaleFailureReason FailureReason { get; }

    public long? SaleId { get; }

    public string? SaleReference { get; }

    public long? CustomerPaymentId { get; }

    public string? CustomerPaymentReference { get; }

    public long TotalAmountMinor { get; }

    public long AllocatedAmountMinor { get; }

    public long RemainingReceivableMinor { get; }

    public long UnallocatedAdvanceMinor { get; }

    public static CreateSaleResult Failed(
        CreateSaleFailureReason failureReason,
        long totalAmountMinor) =>
        new(
            success: false,
            failureReason,
            saleId: null,
            saleReference: null,
            customerPaymentId: null,
            customerPaymentReference: null,
            totalAmountMinor,
            allocatedAmountMinor: 0,
            remainingReceivableMinor: 0,
            unallocatedAdvanceMinor: 0);

    public static CreateSaleResult Succeeded(
        long saleId,
        string saleReference,
        long? customerPaymentId,
        string? customerPaymentReference,
        long totalAmountMinor,
        long allocatedAmountMinor,
        long remainingReceivableMinor,
        long unallocatedAdvanceMinor) =>
        new(
            success: true,
            CreateSaleFailureReason.None,
            saleId,
            saleReference,
            customerPaymentId,
            customerPaymentReference,
            totalAmountMinor,
            allocatedAmountMinor,
            remainingReceivableMinor,
            unallocatedAdvanceMinor);
}
