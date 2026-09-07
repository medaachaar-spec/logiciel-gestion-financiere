namespace FinancialManagement.Application.SupplierPayments;

public sealed class SupplierPaymentObligationLookupResult
{
    private SupplierPaymentObligationLookupResult(
        bool success,
        SupplierPaymentObligationLookupFailureReason failureReason,
        IReadOnlyList<SupplierPaymentObligationLookupItem> items,
        long supplierId,
        int limit)
    {
        Success = success;
        FailureReason = failureReason;
        Items = items;
        SupplierId = supplierId;
        Limit = limit;
    }

    public bool Success { get; }

    public SupplierPaymentObligationLookupFailureReason FailureReason { get; }

    public IReadOnlyList<SupplierPaymentObligationLookupItem> Items { get; }

    public long SupplierId { get; }

    public int Limit { get; }

    public static SupplierPaymentObligationLookupResult Failed(
        SupplierPaymentObligationLookupQuery query,
        SupplierPaymentObligationLookupFailureReason failureReason) =>
        new(false, failureReason, Array.Empty<SupplierPaymentObligationLookupItem>(), query.SupplierId, query.Limit);

    public static SupplierPaymentObligationLookupResult Succeeded(
        SupplierPaymentObligationLookupQuery query,
        IReadOnlyList<SupplierPaymentObligationLookupItem> items) =>
        new(true, SupplierPaymentObligationLookupFailureReason.None, items, query.SupplierId, query.Limit);
}
