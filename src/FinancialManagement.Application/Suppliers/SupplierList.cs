namespace FinancialManagement.Application.Suppliers;

public sealed record SupplierListQuery(
    int PageNumber = 1,
    int PageSize = 50,
    string? SearchText = null);

public sealed record SupplierListItem(
    long Id,
    string Name,
    string? Phone,
    string? Company,
    string? Identifier,
    long CurrentPayableMinorUnits);

public enum SupplierListFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPageNumber,
    InvalidPageSize,
    DataIntegrityViolation
}

public sealed class SupplierListResult
{
    private SupplierListResult(
        bool success,
        SupplierListFailureReason failureReason,
        IReadOnlyList<SupplierListItem> items,
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

    public SupplierListFailureReason FailureReason { get; }

    public IReadOnlyList<SupplierListItem> Items { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages => PageSize <= 0 || TotalCount == 0
        ? 0
        : (int)(((long)TotalCount + PageSize - 1) / PageSize);

    public static SupplierListResult Failed(
        SupplierListQuery query,
        SupplierListFailureReason failureReason) =>
        new(false, failureReason, Array.Empty<SupplierListItem>(), query.PageNumber, query.PageSize, 0);

    public static SupplierListResult Succeeded(
        SupplierListQuery query,
        IReadOnlyList<SupplierListItem> items,
        int totalCount) =>
        new(true, SupplierListFailureReason.None, items, query.PageNumber, query.PageSize, totalCount);
}
