namespace FinancialManagement.Application.Clients;

public enum DeleteClientFailureReason
{
    None,
    NotAuthenticated,
    NotAuthorized,
    InvalidClientId,
    NotFound,
    InUse
}

public sealed class DeleteClientResult
{
    private DeleteClientResult(
        bool success,
        DeleteClientFailureReason failureReason,
        long? clientId)
    {
        Success = success;
        FailureReason = failureReason;
        ClientId = clientId;
    }

    public bool Success { get; }

    public DeleteClientFailureReason FailureReason { get; }

    public long? ClientId { get; }

    public static DeleteClientResult Failed(DeleteClientFailureReason failureReason) =>
        new(false, failureReason, null);

    public static DeleteClientResult Succeeded(long clientId) =>
        new(true, DeleteClientFailureReason.None, clientId);
}
