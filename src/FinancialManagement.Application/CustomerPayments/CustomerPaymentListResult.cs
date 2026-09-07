namespace FinancialManagement.Application.CustomerPayments;

public sealed class CustomerPaymentListResult
{
    private CustomerPaymentListResult(
        bool success,
        CustomerPaymentListFailureReason failureReason,
        IReadOnlyList<CustomerPaymentListItem> items,
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

    public CustomerPaymentListFailureReason FailureReason { get; }

    public IReadOnlyList<CustomerPaymentListItem> Items { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages => PageSize <= 0 || TotalCount == 0
        ? 0
        : (int)(((long)TotalCount + PageSize - 1) / PageSize);

    public static CustomerPaymentListResult Failed(
        CustomerPaymentListQuery query,
        CustomerPaymentListFailureReason failureReason) =>
        new(
            false,
            failureReason,
            Array.Empty<CustomerPaymentListItem>(),
            query.PageNumber,
            query.PageSize,
            0);

    public static CustomerPaymentListResult Succeeded(
        CustomerPaymentListQuery query,
        IReadOnlyList<CustomerPaymentListItem> items,
        int totalCount) =>
        new(true, CustomerPaymentListFailureReason.None, items, query.PageNumber, query.PageSize, totalCount);
}
