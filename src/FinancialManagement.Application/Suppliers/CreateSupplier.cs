namespace FinancialManagement.Application.Suppliers;

public sealed record CreateSupplierCommand(
    string Name,
    string? Phone = null,
    string? Address = null,
    string? Company = null,
    string? Identifier = null,
    string? Observation = null);

public enum CreateSupplierFailureReason
{
    None,
    NotAuthenticated,
    NotAuthorized,
    InvalidName
}

public sealed class CreateSupplierResult
{
    private CreateSupplierResult(
        bool success,
        CreateSupplierFailureReason failureReason,
        long? supplierId)
    {
        Success = success;
        FailureReason = failureReason;
        SupplierId = supplierId;
    }

    public bool Success { get; }

    public CreateSupplierFailureReason FailureReason { get; }

    public long? SupplierId { get; }

    public static CreateSupplierResult Failed(CreateSupplierFailureReason failureReason) =>
        new(false, failureReason, null);

    public static CreateSupplierResult Succeeded(long supplierId) =>
        new(true, CreateSupplierFailureReason.None, supplierId);
}
