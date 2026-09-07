namespace FinancialManagement.Application.SupplierPayments;

public sealed class SupplierPaymentListResult
{
    private SupplierPaymentListResult(
        bool success,
        SupplierPaymentListFailureReason failureReason,
        IReadOnlyList<SupplierPaymentListItem> items,
        int pageNumber,
        int pageSize,
        int totalCount)
    {
        Success = success;
        FailureReason = failureReason;
        Items = items;
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalCount = totalCount;
    }

    public bool Success { get; }

    public SupplierPaymentListFailureReason FailureReason { get; }

    public IReadOnlyList<SupplierPaymentListItem> Items { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages => PageSize <= 0 || TotalCount == 0
        ? 0
        : (int)(((long)TotalCount + PageSize - 1) / PageSize);

    public static SupplierPaymentListResult Failed(
        SupplierPaymentListQuery query,
        SupplierPaymentListFailureReason failureReason) =>
        new(
            false,
            failureReason,
            Array.Empty<SupplierPaymentListItem>(),
            query.PageNumber,
            query.PageSize,
            0);

    public static SupplierPaymentListResult Succeeded(
        SupplierPaymentListQuery query,
        IReadOnlyList<SupplierPaymentListItem> items,
        int totalCount) =>
        new(true, SupplierPaymentListFailureReason.None, items, query.PageNumber, query.PageSize, totalCount);
}
