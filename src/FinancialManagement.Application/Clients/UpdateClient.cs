namespace FinancialManagement.Application.Clients;

public sealed record UpdateClientCommand(
    long ClientId,
    string Name,
    string? Phone = null,
    string? Address = null,
    string? Company = null,
    string? Identifier = null,
    string? Observation = null);

public enum UpdateClientFailureReason
{
    None,
    NotAuthenticated,
    NotAuthorized,
    InvalidClientId,
    InvalidName,
    NotFound
}

public sealed class UpdateClientResult
{
    private UpdateClientResult(
        bool success,
        UpdateClientFailureReason failureReason,
        long? clientId)
    {
        Success = success;
        FailureReason = failureReason;
        ClientId = clientId;
    }

    public bool Success { get; }

    public UpdateClientFailureReason FailureReason { get; }

    public long? ClientId { get; }

    public static UpdateClientResult Failed(UpdateClientFailureReason failureReason) =>
        new(false, failureReason, null);

    public static UpdateClientResult Succeeded(long clientId) =>
        new(true, UpdateClientFailureReason.None, clientId);
}
