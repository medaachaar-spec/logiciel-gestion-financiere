namespace FinancialManagement.Application.Suppliers;

public sealed class SupplierLookupResult
{
    private SupplierLookupResult(
        bool success,
        SupplierLookupFailureReason failureReason,
        IReadOnlyList<SupplierLookupItem> items,
        int limit)
    {
        Success = success;
        FailureReason = failureReason;
        Items = items;
        Limit = limit;
    }

    public bool Success { get; }

    public SupplierLookupFailureReason FailureReason { get; }

    public IReadOnlyList<SupplierLookupItem> Items { get; }

    public int Limit { get; }

    public static SupplierLookupResult Failed(
        SupplierLookupQuery query,
        SupplierLookupFailureReason failureReason) =>
        new(false, failureReason, Array.Empty<SupplierLookupItem>(), query.Limit);

    public static SupplierLookupResult Succeeded(
        SupplierLookupQuery query,
        IReadOnlyList<SupplierLookupItem> items) =>
        new(true, SupplierLookupFailureReason.None, items, query.Limit);
}
