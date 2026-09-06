namespace FinancialManagement.Application.Purchases;

public sealed class PurchaseListResult
{
    private PurchaseListResult(
        bool success,
        PurchaseListFailureReason failureReason,
        IReadOnlyList<PurchaseListItem> items,
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

    public PurchaseListFailureReason FailureReason { get; }

    public IReadOnlyList<PurchaseListItem> Items { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages => PageSize <= 0 || TotalCount == 0
        ? 0
        : (int)(((long)TotalCount + PageSize - 1) / PageSize);

    public static PurchaseListResult Failed(
        PurchaseListQuery query,
        PurchaseListFailureReason failureReason) =>
        new(false, failureReason, Array.Empty<PurchaseListItem>(), query.PageNumber, query.PageSize, 0);

    public static PurchaseListResult Succeeded(
        PurchaseListQuery query,
        IReadOnlyList<PurchaseListItem> items,
        int totalCount) =>
        new(true, PurchaseListFailureReason.None, items, query.PageNumber, query.PageSize, totalCount);
}
