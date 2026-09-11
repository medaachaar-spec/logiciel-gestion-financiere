using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Payments;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.SupplierPayments;

internal sealed class SupplierPaymentObligationReadService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService) : ISupplierPaymentObligationReadService
{
    public async Task<SupplierPaymentObligationPageResult> GetPageAsync(
        SupplierPaymentObligationPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        var failure = await ValidateReadAsync(cancellationToken);
        if (failure is not null)
            return SupplierPaymentObligationPageResult.Failed(query, failure.Value);
        if (query.PageNumber < 1)
            return SupplierPaymentObligationPageResult.Failed(
                query, SupplierPaymentObligationReadFailureReason.InvalidPageNumber);
        if (query.PageSize is < 1 or > 200)
            return SupplierPaymentObligationPageResult.Failed(
                query, SupplierPaymentObligationReadFailureReason.InvalidPageSize);
        if (query.FromDate is DateOnly from && query.ToDate is DateOnly to && from > to)
            return SupplierPaymentObligationPageResult.Failed(
                query, SupplierPaymentObligationReadFailureReason.InvalidDateRange);
        if (query.Status is PaymentObligationStatus status && !Enum.IsDefined(status))
            return SupplierPaymentObligationPageResult.Failed(
                query, SupplierPaymentObligationReadFailureReason.InvalidStatus);

        var skip = ((long)query.PageNumber - 1) * query.PageSize;
        if (skip > int.MaxValue)
            return SupplierPaymentObligationPageResult.Failed(
                query, SupplierPaymentObligationReadFailureReason.InvalidPageNumber);

        return await SqliteReadSnapshot.ExecuteAsync(
            dbContext,
            token => GetPageSnapshotAsync(query, (int)skip, token),
            cancellationToken);
    }

    private async Task<SupplierPaymentObligationPageResult> GetPageSnapshotAsync(
        SupplierPaymentObligationPageQuery query,
        int skip,
        CancellationToken cancellationToken)
    {
        var candidates = await BuildQuery(query.FromDate, query.ToDate, query.SearchText)
            .ToListAsync(cancellationToken);
        var purchaseIds = candidates
            .Where(item => item.TargetType == SupplierPaymentObligationTargetType.Purchase)
            .Select(item => item.TargetId)
            .ToHashSet();
        var initialIds = candidates
            .Where(item => item.TargetType == SupplierPaymentObligationTargetType.InitialSupplierPayable)
            .Select(item => item.TargetId)
            .ToHashSet();
        var integrity = await AllocationIntegrity.ReadSupplierAsync(
            dbContext, purchaseIds, initialIds, null, cancellationToken);
        if (integrity is null)
            return SupplierPaymentObligationPageResult.Failed(
                query, SupplierPaymentObligationReadFailureReason.DataIntegrityViolation);

        var items = new List<SupplierPaymentObligationListItem>(candidates.Count);
        foreach (var obligation in candidates)
        {
            var paid = obligation.TargetType == SupplierPaymentObligationTargetType.Purchase
                ? integrity.Documents.GetValueOrDefault(obligation.TargetId)
                : integrity.Initials.GetValueOrDefault(obligation.TargetId);
            if (!HasValidHeader(obligation, paid))
                return SupplierPaymentObligationPageResult.Failed(
                    query, SupplierPaymentObligationReadFailureReason.DataIntegrityViolation);

            var remaining = obligation.OriginalAmountMinor - paid;
            items.Add(new SupplierPaymentObligationListItem(
                obligation.TargetType,
                obligation.TargetId,
                obligation.Reference,
                obligation.BusinessDate,
                obligation.PartyId,
                obligation.PartyName!,
                obligation.OriginalAmountMinor,
                paid,
                remaining,
                ToStatus(paid, remaining)));
        }

        var filtered = ApplyStatus(items, query.Status)
            .OrderByDescending(item => item.BusinessDate)
            .ThenByDescending(item => item.TargetType == SupplierPaymentObligationTargetType.Purchase)
            .ThenByDescending(item => item.TargetId)
            .ToList();
        var totalCount = filtered.Count;
        var page = filtered.Skip(skip).Take(query.PageSize).ToList();
        return SupplierPaymentObligationPageResult.Succeeded(query, page, totalCount);
    }

    public async Task<SupplierPaymentObligationDetailResult> GetDetailAsync(
        SupplierPaymentObligationTargetType targetType,
        long targetId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = await ValidateReadAsync(cancellationToken);
        if (failure is not null)
            return SupplierPaymentObligationDetailResult.Failed(failure.Value);
        if (targetId <= 0 || !Enum.IsDefined(targetType))
            return SupplierPaymentObligationDetailResult.Failed(
                SupplierPaymentObligationReadFailureReason.InvalidTarget);

        return await SqliteReadSnapshot.ExecuteAsync(
            dbContext,
            token => GetDetailSnapshotAsync(targetType, targetId, token),
            cancellationToken);
    }

    private async Task<SupplierPaymentObligationDetailResult> GetDetailSnapshotAsync(
        SupplierPaymentObligationTargetType targetType,
        long targetId,
        CancellationToken cancellationToken)
    {
        var obligation = await BuildQuery(null, null, null)
            .SingleOrDefaultAsync(
                item => item.TargetType == targetType && item.TargetId == targetId,
                cancellationToken);
        if (obligation is null)
            return SupplierPaymentObligationDetailResult.Failed(
                SupplierPaymentObligationReadFailureReason.NotFound);

        var purchaseIds = targetType == SupplierPaymentObligationTargetType.Purchase
            ? new HashSet<long> { targetId }
            : [];
        var initialIds = targetType == SupplierPaymentObligationTargetType.InitialSupplierPayable
            ? new HashSet<long> { targetId }
            : [];
        var integrity = await AllocationIntegrity.ReadSupplierAsync(
            dbContext, purchaseIds, initialIds, null, cancellationToken);
        if (integrity is null)
            return SupplierPaymentObligationDetailResult.Failed(
                SupplierPaymentObligationReadFailureReason.DataIntegrityViolation);

        var storedHistory = await (
            from allocation in dbContext.SupplierPaymentAllocations.AsNoTracking()
            join payment in dbContext.SupplierPayments.AsNoTracking()
                on allocation.SupplierPaymentId equals payment.Id
            where targetType == SupplierPaymentObligationTargetType.Purchase
                ? allocation.PurchaseId == targetId
                : allocation.InitialSupplierPayableId == targetId
            select new HistoryProjection
            {
                AllocationId = allocation.Id,
                PaymentId = payment.Id,
                PaymentReference = payment.Reference,
                PaymentDate = payment.PaymentDate,
                PaymentCreatedAtUtc = payment.CreatedAtUtc,
                PaymentMethod = payment.PaymentMethod,
                AllocatedAmountMinor = allocation.AmountMinor
            })
            .ToListAsync(cancellationToken);
        storedHistory = storedHistory
            .OrderBy(item => item.PaymentDate)
            .ThenBy(item => item.PaymentCreatedAtUtc)
            .ThenBy(item => item.PaymentId)
            .ThenBy(item => item.AllocationId)
            .ToList();

        var history = new List<SupplierPaymentObligationHistoryItem>(storedHistory.Count);
        var cumulativePaid = 0L;
        foreach (var allocation in storedHistory)
        {
            if (allocation.AllocatedAmountMinor <= 0
                || !Enum.IsDefined(allocation.PaymentMethod)
                || string.IsNullOrWhiteSpace(allocation.PaymentReference)
                || allocation.AllocatedAmountMinor > obligation.OriginalAmountMinor - cumulativePaid)
                return SupplierPaymentObligationDetailResult.Failed(
                    SupplierPaymentObligationReadFailureReason.DataIntegrityViolation);

            cumulativePaid = checked(cumulativePaid + allocation.AllocatedAmountMinor);
            history.Add(new SupplierPaymentObligationHistoryItem(
                allocation.PaymentId,
                allocation.PaymentReference,
                allocation.PaymentDate,
                allocation.PaymentCreatedAtUtc,
                allocation.PaymentMethod,
                allocation.AllocatedAmountMinor,
                cumulativePaid,
                obligation.OriginalAmountMinor - cumulativePaid));
        }

        var trustedPaid = targetType == SupplierPaymentObligationTargetType.Purchase
            ? integrity.Documents.GetValueOrDefault(targetId)
            : integrity.Initials.GetValueOrDefault(targetId);
        if (trustedPaid != cumulativePaid || !HasValidHeader(obligation, trustedPaid))
            return SupplierPaymentObligationDetailResult.Failed(
                SupplierPaymentObligationReadFailureReason.DataIntegrityViolation);

        var remaining = obligation.OriginalAmountMinor - trustedPaid;
        return SupplierPaymentObligationDetailResult.Succeeded(
            new SupplierPaymentObligationDetailItem(
                targetType,
                targetId,
                obligation.Reference,
                obligation.BusinessDate,
                obligation.PartyId,
                obligation.PartyName!,
                obligation.OriginalAmountMinor,
                trustedPaid,
                remaining,
                ToStatus(trustedPaid, remaining),
                history));
    }

    private async Task<SupplierPaymentObligationReadFailureReason?> ValidateReadAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUserSession.IsAuthenticated)
            return SupplierPaymentObligationReadFailureReason.NotAuthenticated;
        return await authorizationService.CanAsync(
                PermissionModule.SupplierPayments,
                PermissionAction.View,
                cancellationToken)
            ? null
            : SupplierPaymentObligationReadFailureReason.Forbidden;
    }

    private IQueryable<ObligationProjection> BuildQuery(
        DateOnly? fromDate,
        DateOnly? toDate,
        string? rawSearchText)
    {
        var searchText = string.IsNullOrWhiteSpace(rawSearchText) ? null : rawSearchText.Trim();
        var purchases =
            from purchase in dbContext.Purchases.AsNoTracking()
            join supplier in dbContext.Suppliers.AsNoTracking()
                on purchase.SupplierId equals supplier.Id into suppliers
            from supplier in suppliers.DefaultIfEmpty()
            where (!fromDate.HasValue || purchase.PurchaseDate >= fromDate.Value)
                && (!toDate.HasValue || purchase.PurchaseDate <= toDate.Value)
                && (searchText == null
                    || purchase.Reference.Contains(searchText)
                    || (supplier != null && supplier.Name.Contains(searchText))
                    || (supplier != null && supplier.Identifier != null && supplier.Identifier.Contains(searchText)))
            select new ObligationProjection
            {
                TargetType = SupplierPaymentObligationTargetType.Purchase,
                TargetSort = 1,
                TargetId = purchase.Id,
                Reference = purchase.Reference,
                BusinessDate = purchase.PurchaseDate,
                PartyId = purchase.SupplierId,
                PartyName = supplier == null ? null : supplier.Name,
                OriginalAmountMinor = purchase.TotalAmountMinor
            };

        var initials =
            from payable in dbContext.InitialSupplierPayables.AsNoTracking()
            join supplier in dbContext.Suppliers.AsNoTracking()
                on payable.SupplierId equals supplier.Id into suppliers
            from supplier in suppliers.DefaultIfEmpty()
            where (!fromDate.HasValue || payable.PayableDate >= fromDate.Value)
                && (!toDate.HasValue || payable.PayableDate <= toDate.Value)
                && (searchText == null
                    || payable.Reference.Contains(searchText)
                    || (supplier != null && supplier.Name.Contains(searchText))
                    || (supplier != null && supplier.Identifier != null && supplier.Identifier.Contains(searchText)))
            select new ObligationProjection
            {
                TargetType = SupplierPaymentObligationTargetType.InitialSupplierPayable,
                TargetSort = 0,
                TargetId = payable.Id,
                Reference = payable.Reference,
                BusinessDate = payable.PayableDate,
                PartyId = payable.SupplierId,
                PartyName = supplier == null ? null : supplier.Name,
                OriginalAmountMinor = payable.InitialAmountMinor
            };

        return purchases.Concat(initials);
    }

    private static IEnumerable<SupplierPaymentObligationListItem> ApplyStatus(
        IEnumerable<SupplierPaymentObligationListItem> query,
        PaymentObligationStatus? status) => status switch
        {
            PaymentObligationStatus exactStatus => query.Where(item => item.Status == exactStatus),
            _ => query
        };

    private static bool HasValidHeader(ObligationProjection item, long paid) =>
        item.TargetId > 0
        && item.PartyId > 0
        && !string.IsNullOrWhiteSpace(item.PartyName)
        && !string.IsNullOrWhiteSpace(item.Reference)
        && item.OriginalAmountMinor > 0
        && paid >= 0
        && paid <= item.OriginalAmountMinor;

    private static PaymentObligationStatus ToStatus(long paid, long remaining) =>
        remaining == 0
            ? PaymentObligationStatus.Settled
            : paid == 0
                ? PaymentObligationStatus.Outstanding
                : PaymentObligationStatus.PartiallyPaid;

    private sealed class ObligationProjection
    {
        public SupplierPaymentObligationTargetType TargetType { get; init; }
        public int TargetSort { get; init; }
        public long TargetId { get; init; }
        public required string Reference { get; init; }
        public DateOnly BusinessDate { get; init; }
        public long PartyId { get; init; }
        public string? PartyName { get; init; }
        public long OriginalAmountMinor { get; init; }
    }

    private sealed class HistoryProjection
    {
        public long AllocationId { get; init; }
        public long PaymentId { get; init; }
        public required string PaymentReference { get; init; }
        public DateOnly PaymentDate { get; init; }
        public DateTimeOffset PaymentCreatedAtUtc { get; init; }
        public PaymentMethod PaymentMethod { get; init; }
        public long AllocatedAmountMinor { get; init; }
    }
}
