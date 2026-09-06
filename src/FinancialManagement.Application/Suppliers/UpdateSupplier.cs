namespace FinancialManagement.Application.Suppliers;

public sealed record UpdateSupplierCommand(
    long SupplierId,
    string Name,
    string? Phone = null,
    string? Address = null,
    string? Company = null,
    string? Identifier = null,
    string? Observation = null);

public enum UpdateSupplierFailureReason
{
    None,
    NotAuthenticated,
    NotAuthorized,
    InvalidSupplierId,
    InvalidName,
    NotFound
}

public sealed class UpdateSupplierResult
{
    private UpdateSupplierResult(
        bool success,
        UpdateSupplierFailureReason failureReason,
        long? supplierId)
    {
        Success = success;
        FailureReason = failureReason;
        SupplierId = supplierId;
    }

    public bool Success { get; }

    public UpdateSupplierFailureReason FailureReason { get; }

    public long? SupplierId { get; }

    public static UpdateSupplierResult Failed(UpdateSupplierFailureReason failureReason) =>
        new(false, failureReason, null);

    public static UpdateSupplierResult Succeeded(long supplierId) =>
        new(true, UpdateSupplierFailureReason.None, supplierId);
}
