namespace FinancialManagement.Application.Suppliers;

public sealed record SupplierDetailItem(
    long Id,
    string Name,
    string? Phone,
    string? Address,
    string? Company,
    string? Identifier,
    string? Observation,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public enum SupplierDetailFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidSupplierId,
    NotFound
}

public sealed class SupplierDetailResult
{
    private SupplierDetailResult(
        bool success,
        SupplierDetailFailureReason failureReason,
        SupplierDetailItem? item)
    {
        Success = success;
        FailureReason = failureReason;
        Item = item;
    }

    public bool Success { get; }

    public SupplierDetailFailureReason FailureReason { get; }

    public SupplierDetailItem? Item { get; }

    public static SupplierDetailResult Failed(SupplierDetailFailureReason failureReason) =>
        new(false, failureReason, null);

    public static SupplierDetailResult Succeeded(SupplierDetailItem item) =>
        new(true, SupplierDetailFailureReason.None, item);
}
