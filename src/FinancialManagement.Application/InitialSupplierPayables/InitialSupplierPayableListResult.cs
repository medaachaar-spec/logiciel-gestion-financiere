namespace FinancialManagement.Application.InitialSupplierPayables;

public sealed class InitialSupplierPayableListResult
{
    private InitialSupplierPayableListResult(InitialSupplierPayableListQuery query, InitialSupplierPayableListFailureReason reason,
        IReadOnlyList<InitialSupplierPayableListItem> items, int totalCount)
    {
        PageNumber = query.PageNumber;
        PageSize = query.PageSize;
        FailureReason = reason;
        Items = items;
        TotalCount = totalCount;
    }

    public bool Success => FailureReason == InitialSupplierPayableListFailureReason.None;
    public InitialSupplierPayableListFailureReason FailureReason { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public IReadOnlyList<InitialSupplierPayableListItem> Items { get; }
    public int TotalCount { get; }
    public int TotalPages => PageSize > 0 ? (int)(((long)TotalCount + PageSize - 1) / PageSize) : 0;

    public static InitialSupplierPayableListResult Succeeded(InitialSupplierPayableListQuery query,
        IReadOnlyList<InitialSupplierPayableListItem> items, int totalCount) => new(query, InitialSupplierPayableListFailureReason.None, items, totalCount);
    public static InitialSupplierPayableListResult Failed(InitialSupplierPayableListQuery query, InitialSupplierPayableListFailureReason reason) => new(query, reason, [], 0);
}
