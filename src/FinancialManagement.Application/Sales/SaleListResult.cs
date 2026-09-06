namespace FinancialManagement.Application.Sales;

public sealed class SaleListResult
{
    private SaleListResult(
        bool success,
        SaleListFailureReason failureReason,
        IReadOnlyList<SaleListItem> items,
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

    public SaleListFailureReason FailureReason { get; }

    public IReadOnlyList<SaleListItem> Items { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages => PageSize <= 0 || TotalCount == 0
        ? 0
        : (int)(((long)TotalCount + PageSize - 1) / PageSize);

    public static SaleListResult Failed(
        SaleListQuery query,
        SaleListFailureReason failureReason) =>
        new(false, failureReason, Array.Empty<SaleListItem>(), query.PageNumber, query.PageSize, 0);

    public static SaleListResult Succeeded(
        SaleListQuery query,
        IReadOnlyList<SaleListItem> items,
        int totalCount) =>
        new(true, SaleListFailureReason.None, items, query.PageNumber, query.PageSize, totalCount);
}
