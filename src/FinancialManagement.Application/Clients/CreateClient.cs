namespace FinancialManagement.Application.Clients;

public sealed record CreateClientCommand(
    string Name,
    string? Phone = null,
    string? Address = null,
    string? Company = null,
    string? Identifier = null,
    string? Observation = null);

public enum CreateClientFailureReason
{
    None,
    NotAuthenticated,
    NotAuthorized,
    InvalidName
}

public sealed class CreateClientResult
{
    private CreateClientResult(
        bool success,
        CreateClientFailureReason failureReason,
        long? clientId)
    {
        Success = success;
        FailureReason = failureReason;
        ClientId = clientId;
    }

    public bool Success { get; }

    public CreateClientFailureReason FailureReason { get; }

    public long? ClientId { get; }

    public static CreateClientResult Failed(CreateClientFailureReason failureReason) =>
        new(false, failureReason, null);

    public static CreateClientResult Succeeded(long clientId) =>
        new(true, CreateClientFailureReason.None, clientId);
}
