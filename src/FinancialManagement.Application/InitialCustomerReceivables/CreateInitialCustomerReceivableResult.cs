namespace FinancialManagement.Application.InitialCustomerReceivables;

public sealed class CreateInitialCustomerReceivableResult
{
    private CreateInitialCustomerReceivableResult(
        bool success,
        CreateInitialCustomerReceivableFailureReason failureReason,
        long? initialCustomerReceivableId,
        string? reference,
        long initialAmountMinor)
    {
        Success = success;
        FailureReason = failureReason;
        InitialCustomerReceivableId = initialCustomerReceivableId;
        Reference = reference;
        InitialAmountMinor = initialAmountMinor;
    }

    public bool Success { get; }

    public CreateInitialCustomerReceivableFailureReason FailureReason { get; }

    public long? InitialCustomerReceivableId { get; }

    public string? Reference { get; }

    public long InitialAmountMinor { get; }

    public static CreateInitialCustomerReceivableResult Failed(
        CreateInitialCustomerReceivableFailureReason failureReason,
        long initialAmountMinor) =>
        new(false, failureReason, null, null, initialAmountMinor);

    public static CreateInitialCustomerReceivableResult Succeeded(
        long id,
        string reference,
        long initialAmountMinor) =>
        new(true, CreateInitialCustomerReceivableFailureReason.None, id, reference, initialAmountMinor);
}
