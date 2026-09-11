using FinancialManagement.Application.Payments;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.CustomerPayments;

public interface ICustomerPaymentObligationReadService
{
    Task<CustomerPaymentObligationPageResult> GetPageAsync(
        CustomerPaymentObligationPageQuery query,
        CancellationToken cancellationToken = default);

    Task<CustomerPaymentObligationDetailResult> GetDetailAsync(
        CustomerPaymentObligationTargetType targetType,
        long targetId,
        CancellationToken cancellationToken = default);
}

public sealed record CustomerPaymentObligationPageQuery(
    int PageNumber = 1,
    int PageSize = 50,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? SearchText = null,
    PaymentObligationStatus? Status = null);

public sealed record CustomerPaymentObligationListItem(
    CustomerPaymentObligationTargetType TargetType,
    long TargetId,
    string Reference,
    DateOnly BusinessDate,
    long? ClientId,
    string? ClientName,
    long OriginalAmountMinor,
    long PaidAmountMinor,
    long RemainingAmountMinor,
    PaymentObligationStatus Status);

public sealed class CustomerPaymentObligationPageResult
{
    private CustomerPaymentObligationPageResult(
        bool success,
        CustomerPaymentObligationReadFailureReason failureReason,
        IReadOnlyList<CustomerPaymentObligationListItem> items,
        CustomerPaymentObligationPageQuery query,
        int totalCount)
    {
        Success = success;
        FailureReason = failureReason;
        Items = items;
        PageNumber = query.PageNumber;
        PageSize = query.PageSize;
        TotalCount = totalCount;
    }

    public bool Success { get; }
    public CustomerPaymentObligationReadFailureReason FailureReason { get; }
    public IReadOnlyList<CustomerPaymentObligationListItem> Items { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public int TotalCount { get; }
    public int TotalPages => PageSize <= 0 || TotalCount == 0
        ? 0
        : (int)(((long)TotalCount + PageSize - 1) / PageSize);

    public static CustomerPaymentObligationPageResult Failed(
        CustomerPaymentObligationPageQuery query,
        CustomerPaymentObligationReadFailureReason reason) =>
        new(false, reason, [], query, 0);

    public static CustomerPaymentObligationPageResult Succeeded(
        CustomerPaymentObligationPageQuery query,
        IReadOnlyList<CustomerPaymentObligationListItem> items,
        int totalCount) =>
        new(true, CustomerPaymentObligationReadFailureReason.None, items, query, totalCount);
}

public sealed record CustomerPaymentObligationHistoryItem(
    long AllocationId,
    long PaymentId,
    string PaymentReference,
    DateOnly PaymentDate,
    DateTimeOffset PaymentCreatedAtUtc,
    PaymentMethod PaymentMethod,
    long AllocatedAmountMinor,
    long CumulativePaidAmountMinor,
    long RemainingAfterPaymentMinor);

public sealed record CustomerPaymentObligationDetailItem(
    CustomerPaymentObligationTargetType TargetType,
    long TargetId,
    string Reference,
    DateOnly BusinessDate,
    long? ClientId,
    string? ClientName,
    long OriginalAmountMinor,
    long PaidAmountMinor,
    long RemainingAmountMinor,
    PaymentObligationStatus Status,
    IReadOnlyList<CustomerPaymentObligationHistoryItem> History);

public sealed class CustomerPaymentObligationDetailResult
{
    private CustomerPaymentObligationDetailResult(
        bool success,
        CustomerPaymentObligationReadFailureReason failureReason,
        CustomerPaymentObligationDetailItem? item)
    {
        Success = success;
        FailureReason = failureReason;
        Item = item;
    }

    public bool Success { get; }
    public CustomerPaymentObligationReadFailureReason FailureReason { get; }
    public CustomerPaymentObligationDetailItem? Item { get; }

    public static CustomerPaymentObligationDetailResult Failed(
        CustomerPaymentObligationReadFailureReason reason) => new(false, reason, null);

    public static CustomerPaymentObligationDetailResult Succeeded(
        CustomerPaymentObligationDetailItem item) =>
        new(true, CustomerPaymentObligationReadFailureReason.None, item);
}

public enum CustomerPaymentObligationReadFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPageNumber,
    InvalidPageSize,
    InvalidDateRange,
    InvalidStatus,
    InvalidTarget,
    NotFound,
    DataIntegrityViolation
}
