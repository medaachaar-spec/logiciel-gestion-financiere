namespace FinancialManagement.Application.Suppliers;

public enum DeleteSupplierFailureReason
{
    None,
    NotAuthenticated,
    NotAuthorized,
    InvalidSupplierId,
    NotFound,
    InUse
}

public sealed class DeleteSupplierResult
{
    private DeleteSupplierResult(
        bool success,
        DeleteSupplierFailureReason failureReason,
        long? supplierId)
    {
        Success = success;
        FailureReason = failureReason;
        SupplierId = supplierId;
    }

    public bool Success { get; }

    public DeleteSupplierFailureReason FailureReason { get; }

    public long? SupplierId { get; }

    public static DeleteSupplierResult Failed(DeleteSupplierFailureReason failureReason) =>
        new(false, failureReason, null);

    public static DeleteSupplierResult Succeeded(long supplierId) =>
        new(true, DeleteSupplierFailureReason.None, supplierId);
}
