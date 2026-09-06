namespace FinancialManagement.Application.Clients;

public sealed class ClientLookupResult
{
    private ClientLookupResult(
        bool success,
        ClientLookupFailureReason failureReason,
        IReadOnlyList<ClientLookupItem> items,
        int limit)
    {
        Success = success;
        FailureReason = failureReason;
        Items = items;
        Limit = limit;
    }

    public bool Success { get; }

    public ClientLookupFailureReason FailureReason { get; }

    public IReadOnlyList<ClientLookupItem> Items { get; }

    public int Limit { get; }

    public static ClientLookupResult Failed(
        ClientLookupQuery query,
        ClientLookupFailureReason failureReason) =>
        new(false, failureReason, Array.Empty<ClientLookupItem>(), query.Limit);

    public static ClientLookupResult Succeeded(
        ClientLookupQuery query,
        IReadOnlyList<ClientLookupItem> items) =>
        new(true, ClientLookupFailureReason.None, items, query.Limit);
}
