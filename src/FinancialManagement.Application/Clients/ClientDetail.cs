namespace FinancialManagement.Application.Clients;

public sealed record ClientDetailItem(
    long Id,
    string Name,
    string? Phone,
    string? Address,
    string? Company,
    string? Identifier,
    string? Observation,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public enum ClientDetailFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidClientId,
    NotFound
}

public sealed class ClientDetailResult
{
    private ClientDetailResult(
        bool success,
        ClientDetailFailureReason failureReason,
        ClientDetailItem? item)
    {
        Success = success;
        FailureReason = failureReason;
        Item = item;
    }

    public bool Success { get; }

    public ClientDetailFailureReason FailureReason { get; }

    public ClientDetailItem? Item { get; }

    public static ClientDetailResult Failed(ClientDetailFailureReason failureReason) =>
        new(false, failureReason, null);

    public static ClientDetailResult Succeeded(ClientDetailItem item) =>
        new(true, ClientDetailFailureReason.None, item);
}
