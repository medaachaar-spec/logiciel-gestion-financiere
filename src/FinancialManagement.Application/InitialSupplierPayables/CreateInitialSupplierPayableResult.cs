namespace FinancialManagement.Application.InitialSupplierPayables;

public sealed class CreateInitialSupplierPayableResult
{
    private CreateInitialSupplierPayableResult(
        bool success,
        CreateInitialSupplierPayableFailureReason failureReason,
        long? initialSupplierPayableId,
        string? reference,
        long initialAmountMinor)
    {
        Success = success;
        FailureReason = failureReason;
        InitialSupplierPayableId = initialSupplierPayableId;
        Reference = reference;
        InitialAmountMinor = initialAmountMinor;
    }

    public bool Success { get; }

    public CreateInitialSupplierPayableFailureReason FailureReason { get; }

    public long? InitialSupplierPayableId { get; }

    public string? Reference { get; }

    public long InitialAmountMinor { get; }

    public static CreateInitialSupplierPayableResult Failed(
        CreateInitialSupplierPayableFailureReason failureReason,
        long initialAmountMinor) =>
        new(false, failureReason, null, null, initialAmountMinor);

    public static CreateInitialSupplierPayableResult Succeeded(
        long id,
        string reference,
        long initialAmountMinor) =>
        new(true, CreateInitialSupplierPayableFailureReason.None, id, reference, initialAmountMinor);
}
