namespace FinancialManagement.Application.InitialCustomerReceivables;

public sealed class InitialCustomerReceivableListResult
{
    private InitialCustomerReceivableListResult(InitialCustomerReceivableListQuery query, InitialCustomerReceivableListFailureReason reason,
        IReadOnlyList<InitialCustomerReceivableListItem> items, int totalCount)
    {
        PageNumber = query.PageNumber;
        PageSize = query.PageSize;
        FailureReason = reason;
        Items = items;
        TotalCount = totalCount;
    }

    public bool Success => FailureReason == InitialCustomerReceivableListFailureReason.None;
    public InitialCustomerReceivableListFailureReason FailureReason { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public IReadOnlyList<InitialCustomerReceivableListItem> Items { get; }
    public int TotalCount { get; }
    public int TotalPages => PageSize > 0 ? (int)(((long)TotalCount + PageSize - 1) / PageSize) : 0;

    public static InitialCustomerReceivableListResult Succeeded(InitialCustomerReceivableListQuery query,
        IReadOnlyList<InitialCustomerReceivableListItem> items, int totalCount) => new(query, InitialCustomerReceivableListFailureReason.None, items, totalCount);
    public static InitialCustomerReceivableListResult Failed(InitialCustomerReceivableListQuery query, InitialCustomerReceivableListFailureReason reason) => new(query, reason, [], 0);
}
