namespace FinancialManagement.Application.Clients;

public sealed record ClientListQuery(
    int PageNumber = 1,
    int PageSize = 50,
    string? SearchText = null);

public sealed record ClientListItem(
    long Id,
    string Name,
    string? Phone,
    string? Company,
    string? Identifier,
    long CurrentReceivableMinorUnits);

public enum ClientListFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPageNumber,
    InvalidPageSize,
    DataIntegrityViolation
}

public sealed class ClientListResult
{
    private ClientListResult(
        bool success,
        ClientListFailureReason failureReason,
        IReadOnlyList<ClientListItem> items,
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

    public ClientListFailureReason FailureReason { get; }

    public IReadOnlyList<ClientListItem> Items { get; }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages => PageSize <= 0 || TotalCount == 0
        ? 0
        : (int)(((long)TotalCount + PageSize - 1) / PageSize);

    public static ClientListResult Failed(
        ClientListQuery query,
        ClientListFailureReason failureReason) =>
        new(false, failureReason, Array.Empty<ClientListItem>(), query.PageNumber, query.PageSize, 0);

    public static ClientListResult Succeeded(
        ClientListQuery query,
        IReadOnlyList<ClientListItem> items,
        int totalCount) =>
        new(true, ClientListFailureReason.None, items, query.PageNumber, query.PageSize, totalCount);
}
