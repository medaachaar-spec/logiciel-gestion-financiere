namespace FinancialManagement.Application.Expenses;

public sealed class ExpenseListResult
{
    private ExpenseListResult(
        bool success,
        ExpenseListFailureReason failureReason,
        IReadOnlyList<ExpenseListItem> items,
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

    public ExpenseListFailureReason FailureReason { get; }

    public IReadOnlyList<ExpenseListItem> Items { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages => PageSize <= 0 || TotalCount == 0
        ? 0
        : (int)(((long)TotalCount + PageSize - 1) / PageSize);

    public static ExpenseListResult Failed(
        ExpenseListQuery query,
        ExpenseListFailureReason failureReason) =>
        new(false, failureReason, Array.Empty<ExpenseListItem>(), query.PageNumber, query.PageSize, 0);

    public static ExpenseListResult Succeeded(
        ExpenseListQuery query,
        IReadOnlyList<ExpenseListItem> items,
        int totalCount) =>
        new(true, ExpenseListFailureReason.None, items, query.PageNumber, query.PageSize, totalCount);
}
