using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.CustomerPayments;

public interface ICustomerPaymentReceiptService
{
    Task<CustomerPaymentReceiptResult> GetAsync(
        long customerPaymentId,
        CancellationToken cancellationToken = default);
}

public sealed record CustomerPaymentReceiptAllocationItem(
    CustomerPaymentAllocationTargetType TargetType,
    string TargetReference,
    long OriginalObligationAmountMinor,
    long AllocatedAmountMinor,
    long CumulativePaidAfterAllocationMinor,
    long RemainingAfterAllocationMinor);

public sealed record CustomerPaymentReceiptItem(
    string PaymentReference,
    DateOnly PaymentDate,
    DateTimeOffset PaymentCreatedAtUtc,
    string CustomerName,
    long PaymentAmountMinor,
    PaymentMethod PaymentMethod,
    long UnallocatedAmountMinor,
    IReadOnlyList<CustomerPaymentReceiptAllocationItem> Allocations);

public sealed class CustomerPaymentReceiptResult
{
    private CustomerPaymentReceiptResult(
        bool success,
        CustomerPaymentReceiptFailureReason failureReason,
        CustomerPaymentReceiptItem? item)
    {
        Success = success;
        FailureReason = failureReason;
        Item = item;
    }

    public bool Success { get; }
    public CustomerPaymentReceiptFailureReason FailureReason { get; }
    public CustomerPaymentReceiptItem? Item { get; }

    public static CustomerPaymentReceiptResult Succeeded(CustomerPaymentReceiptItem item) =>
        new(true, CustomerPaymentReceiptFailureReason.None, item);

    public static CustomerPaymentReceiptResult Failed(CustomerPaymentReceiptFailureReason reason) =>
        new(false, reason, null);
}

public enum CustomerPaymentReceiptFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPaymentId,
    NotFound,
    DataIntegrityViolation
}
