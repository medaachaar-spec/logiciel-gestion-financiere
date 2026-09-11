using FinancialManagement.Application.Payments;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.SupplierPayments;

public interface ISupplierPaymentObligationReadService
{
    Task<SupplierPaymentObligationPageResult> GetPageAsync(
        SupplierPaymentObligationPageQuery query,
        CancellationToken cancellationToken = default);

    Task<SupplierPaymentObligationDetailResult> GetDetailAsync(
        SupplierPaymentObligationTargetType targetType,
        long targetId,
        CancellationToken cancellationToken = default);
}

public sealed record SupplierPaymentObligationPageQuery(
    int PageNumber = 1,
    int PageSize = 50,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? SearchText = null,
    PaymentObligationStatus? Status = null);

public sealed record SupplierPaymentObligationListItem(
    SupplierPaymentObligationTargetType TargetType,
    long TargetId,
    string Reference,
    DateOnly BusinessDate,
    long SupplierId,
    string SupplierName,
    long OriginalAmountMinor,
    long PaidAmountMinor,
    long RemainingAmountMinor,
    PaymentObligationStatus Status);

public sealed class SupplierPaymentObligationPageResult
{
    private SupplierPaymentObligationPageResult(
        bool success,
        SupplierPaymentObligationReadFailureReason failureReason,
        IReadOnlyList<SupplierPaymentObligationListItem> items,
        SupplierPaymentObligationPageQuery query,
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
    public SupplierPaymentObligationReadFailureReason FailureReason { get; }
    public IReadOnlyList<SupplierPaymentObligationListItem> Items { get; }
    public int PageNumber { get; }
    public int PageSize { get; }
    public int TotalCount { get; }
    public int TotalPages => PageSize <= 0 || TotalCount == 0
        ? 0
        : (int)(((long)TotalCount + PageSize - 1) / PageSize);

    public static SupplierPaymentObligationPageResult Failed(
        SupplierPaymentObligationPageQuery query,
        SupplierPaymentObligationReadFailureReason reason) =>
        new(false, reason, [], query, 0);

    public static SupplierPaymentObligationPageResult Succeeded(
        SupplierPaymentObligationPageQuery query,
        IReadOnlyList<SupplierPaymentObligationListItem> items,
        int totalCount) =>
        new(true, SupplierPaymentObligationReadFailureReason.None, items, query, totalCount);
}

public sealed record SupplierPaymentObligationHistoryItem(
    long PaymentId,
    string PaymentReference,
    DateOnly PaymentDate,
    DateTimeOffset PaymentCreatedAtUtc,
    PaymentMethod PaymentMethod,
    long AllocatedAmountMinor,
    long CumulativePaidAmountMinor,
    long RemainingAfterPaymentMinor);

public sealed record SupplierPaymentObligationDetailItem(
    SupplierPaymentObligationTargetType TargetType,
    long TargetId,
    string Reference,
    DateOnly BusinessDate,
    long SupplierId,
    string SupplierName,
    long OriginalAmountMinor,
    long PaidAmountMinor,
    long RemainingAmountMinor,
    PaymentObligationStatus Status,
    IReadOnlyList<SupplierPaymentObligationHistoryItem> History);

public sealed class SupplierPaymentObligationDetailResult
{
    private SupplierPaymentObligationDetailResult(
        bool success,
        SupplierPaymentObligationReadFailureReason failureReason,
        SupplierPaymentObligationDetailItem? item)
    {
        Success = success;
        FailureReason = failureReason;
        Item = item;
    }

    public bool Success { get; }
    public SupplierPaymentObligationReadFailureReason FailureReason { get; }
    public SupplierPaymentObligationDetailItem? Item { get; }

    public static SupplierPaymentObligationDetailResult Failed(
        SupplierPaymentObligationReadFailureReason reason) => new(false, reason, null);

    public static SupplierPaymentObligationDetailResult Succeeded(
        SupplierPaymentObligationDetailItem item) =>
        new(true, SupplierPaymentObligationReadFailureReason.None, item);
}

public enum SupplierPaymentObligationReadFailureReason
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
