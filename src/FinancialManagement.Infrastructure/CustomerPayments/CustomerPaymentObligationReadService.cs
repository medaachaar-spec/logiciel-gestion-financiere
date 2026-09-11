using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.Payments;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.CustomerPayments;

internal sealed class CustomerPaymentObligationReadService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService) : ICustomerPaymentObligationReadService
{
    public async Task<CustomerPaymentObligationPageResult> GetPageAsync(
        CustomerPaymentObligationPageQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        var failure = await ValidateReadAsync(cancellationToken);
        if (failure is not null)
            return CustomerPaymentObligationPageResult.Failed(query, failure.Value);
        if (query.PageNumber < 1)
            return CustomerPaymentObligationPageResult.Failed(
                query, CustomerPaymentObligationReadFailureReason.InvalidPageNumber);
        if (query.PageSize is < 1 or > 200)
            return CustomerPaymentObligationPageResult.Failed(
                query, CustomerPaymentObligationReadFailureReason.InvalidPageSize);
        if (query.FromDate is DateOnly from && query.ToDate is DateOnly to && from > to)
            return CustomerPaymentObligationPageResult.Failed(
                query, CustomerPaymentObligationReadFailureReason.InvalidDateRange);
        if (query.Status is PaymentObligationStatus status && !Enum.IsDefined(status))
            return CustomerPaymentObligationPageResult.Failed(
                query, CustomerPaymentObligationReadFailureReason.InvalidStatus);

        var skip = ((long)query.PageNumber - 1) * query.PageSize;
        if (skip > int.MaxValue)
            return CustomerPaymentObligationPageResult.Failed(
                query, CustomerPaymentObligationReadFailureReason.InvalidPageNumber);

        return await SqliteReadSnapshot.ExecuteAsync(
            dbContext,
            token => GetPageSnapshotAsync(query, (int)skip, token),
            cancellationToken);
    }

    private async Task<CustomerPaymentObligationPageResult> GetPageSnapshotAsync(
        CustomerPaymentObligationPageQuery query,
        int skip,
        CancellationToken cancellationToken)
    {
        var candidates = await BuildQuery(query.FromDate, query.ToDate, query.SearchText)
            .ToListAsync(cancellationToken);
        var saleIds = candidates
            .Where(item => item.TargetType == CustomerPaymentObligationTargetType.Sale)
            .Select(item => item.TargetId)
            .ToHashSet();
        var initialIds = candidates
            .Where(item => item.TargetType == CustomerPaymentObligationTargetType.InitialCustomerReceivable)
            .Select(item => item.TargetId)
            .ToHashSet();
        var integrity = await AllocationIntegrity.ReadCustomerAsync(
            dbContext, saleIds, initialIds, null, cancellationToken);
        if (integrity is null)
            return CustomerPaymentObligationPageResult.Failed(
                query, CustomerPaymentObligationReadFailureReason.DataIntegrityViolation);

        var items = new List<CustomerPaymentObligationListItem>(candidates.Count);
        foreach (var obligation in candidates)
        {
            var paid = obligation.TargetType == CustomerPaymentObligationTargetType.Sale
                ? integrity.Documents.GetValueOrDefault(obligation.TargetId)
                : integrity.Initials.GetValueOrDefault(obligation.TargetId);
            if (!HasValidHeader(obligation, paid))
                return CustomerPaymentObligationPageResult.Failed(
                    query, CustomerPaymentObligationReadFailureReason.DataIntegrityViolation);

            var remaining = obligation.OriginalAmountMinor - paid;
            items.Add(new CustomerPaymentObligationListItem(
                obligation.TargetType,
                obligation.TargetId,
                obligation.Reference,
                obligation.BusinessDate,
                obligation.PartyId,
                obligation.PartyName,
                obligation.OriginalAmountMinor,
                paid,
                remaining,
                ToStatus(paid, remaining)));
        }

        var filtered = ApplyStatus(items, query.Status)
            .OrderByDescending(item => item.BusinessDate)
            .ThenByDescending(item => item.TargetType == CustomerPaymentObligationTargetType.Sale)
            .ThenByDescending(item => item.TargetId)
            .ToList();
        var totalCount = filtered.Count;
        var page = filtered.Skip(skip).Take(query.PageSize).ToList();
        return CustomerPaymentObligationPageResult.Succeeded(query, page, totalCount);
    }

    public async Task<CustomerPaymentObligationDetailResult> GetDetailAsync(
        CustomerPaymentObligationTargetType targetType,
        long targetId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var failure = await ValidateReadAsync(cancellationToken);
        if (failure is not null)
            return CustomerPaymentObligationDetailResult.Failed(failure.Value);
        if (targetId <= 0 || !Enum.IsDefined(targetType))
            return CustomerPaymentObligationDetailResult.Failed(
                CustomerPaymentObligationReadFailureReason.InvalidTarget);

        return await SqliteReadSnapshot.ExecuteAsync(
            dbContext,
            token => GetDetailSnapshotAsync(targetType, targetId, token),
            cancellationToken);
    }

    private async Task<CustomerPaymentObligationDetailResult> GetDetailSnapshotAsync(
        CustomerPaymentObligationTargetType targetType,
        long targetId,
        CancellationToken cancellationToken)
    {
        var obligation = await BuildQuery(null, null, null)
            .SingleOrDefaultAsync(
                item => item.TargetType == targetType && item.TargetId == targetId,
                cancellationToken);
        if (obligation is null)
            return CustomerPaymentObligationDetailResult.Failed(
                CustomerPaymentObligationReadFailureReason.NotFound);

        var saleIds = targetType == CustomerPaymentObligationTargetType.Sale
            ? new HashSet<long> { targetId }
            : [];
        var initialIds = targetType == CustomerPaymentObligationTargetType.InitialCustomerReceivable
            ? new HashSet<long> { targetId }
            : [];
        var integrity = await AllocationIntegrity.ReadCustomerAsync(
            dbContext, saleIds, initialIds, null, cancellationToken);
        if (integrity is null)
            return CustomerPaymentObligationDetailResult.Failed(
                CustomerPaymentObligationReadFailureReason.DataIntegrityViolation);

        var storedHistory = await (
            from allocation in dbContext.CustomerPaymentAllocations.AsNoTracking()
            join payment in dbContext.CustomerPayments.AsNoTracking()
                on allocation.CustomerPaymentId equals payment.Id
            where targetType == CustomerPaymentObligationTargetType.Sale
                ? allocation.SaleId == targetId
                : allocation.InitialCustomerReceivableId == targetId
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

        var history = new List<CustomerPaymentObligationHistoryItem>(storedHistory.Count);
        var cumulativePaid = 0L;
        foreach (var allocation in storedHistory)
        {
            if (allocation.AllocatedAmountMinor <= 0
                || !Enum.IsDefined(allocation.PaymentMethod)
                || string.IsNullOrWhiteSpace(allocation.PaymentReference)
                || allocation.AllocatedAmountMinor > obligation.OriginalAmountMinor - cumulativePaid)
                return CustomerPaymentObligationDetailResult.Failed(
                    CustomerPaymentObligationReadFailureReason.DataIntegrityViolation);

            cumulativePaid = checked(cumulativePaid + allocation.AllocatedAmountMinor);
            history.Add(new CustomerPaymentObligationHistoryItem(
                allocation.PaymentId,
                allocation.PaymentReference,
                allocation.PaymentDate,
                allocation.PaymentCreatedAtUtc,
                allocation.PaymentMethod,
                allocation.AllocatedAmountMinor,
                cumulativePaid,
                obligation.OriginalAmountMinor - cumulativePaid));
        }

        var trustedPaid = targetType == CustomerPaymentObligationTargetType.Sale
            ? integrity.Documents.GetValueOrDefault(targetId)
            : integrity.Initials.GetValueOrDefault(targetId);
        if (trustedPaid != cumulativePaid || !HasValidHeader(obligation, trustedPaid))
            return CustomerPaymentObligationDetailResult.Failed(
                CustomerPaymentObligationReadFailureReason.DataIntegrityViolation);

        var remaining = obligation.OriginalAmountMinor - trustedPaid;
        return CustomerPaymentObligationDetailResult.Succeeded(
            new CustomerPaymentObligationDetailItem(
                targetType,
                targetId,
                obligation.Reference,
                obligation.BusinessDate,
                obligation.PartyId,
                obligation.PartyName,
                obligation.OriginalAmountMinor,
                trustedPaid,
                remaining,
                ToStatus(trustedPaid, remaining),
                history));
    }

    private async Task<CustomerPaymentObligationReadFailureReason?> ValidateReadAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUserSession.IsAuthenticated)
            return CustomerPaymentObligationReadFailureReason.NotAuthenticated;
        return await authorizationService.CanAsync(
                PermissionModule.CustomerPayments,
                PermissionAction.View,
                cancellationToken)
            ? null
            : CustomerPaymentObligationReadFailureReason.Forbidden;
    }

    private IQueryable<ObligationProjection> BuildQuery(
        DateOnly? fromDate,
        DateOnly? toDate,
        string? rawSearchText)
    {
        var searchText = string.IsNullOrWhiteSpace(rawSearchText) ? null : rawSearchText.Trim();
        var sales =
            from sale in dbContext.Sales.AsNoTracking()
            join client in dbContext.Clients.AsNoTracking()
                on sale.ClientId equals (long?)client.Id into clients
            from client in clients.DefaultIfEmpty()
            where (!fromDate.HasValue || sale.SaleDate >= fromDate.Value)
                && (!toDate.HasValue || sale.SaleDate <= toDate.Value)
                && (searchText == null
                    || sale.Reference.Contains(searchText)
                    || (client != null && client.Name.Contains(searchText))
                    || (client != null && client.Identifier != null && client.Identifier.Contains(searchText)))
            select new ObligationProjection
            {
                TargetType = CustomerPaymentObligationTargetType.Sale,
                TargetSort = 1,
                TargetId = sale.Id,
                Reference = sale.Reference,
                BusinessDate = sale.SaleDate,
                PartyId = sale.ClientId,
                PartyName = client == null ? null : client.Name,
                OriginalAmountMinor = sale.TotalAmountMinor
            };

        var initials =
            from receivable in dbContext.InitialCustomerReceivables.AsNoTracking()
            join client in dbContext.Clients.AsNoTracking()
                on receivable.ClientId equals client.Id into clients
            from client in clients.DefaultIfEmpty()
            where (!fromDate.HasValue || receivable.ReceivableDate >= fromDate.Value)
                && (!toDate.HasValue || receivable.ReceivableDate <= toDate.Value)
                && (searchText == null
                    || receivable.Reference.Contains(searchText)
                    || (client != null && client.Name.Contains(searchText))
                    || (client != null && client.Identifier != null && client.Identifier.Contains(searchText)))
            select new ObligationProjection
            {
                TargetType = CustomerPaymentObligationTargetType.InitialCustomerReceivable,
                TargetSort = 0,
                TargetId = receivable.Id,
                Reference = receivable.Reference,
                BusinessDate = receivable.ReceivableDate,
                PartyId = receivable.ClientId,
                PartyName = client == null ? null : client.Name,
                OriginalAmountMinor = receivable.InitialAmountMinor
            };

        return sales.Concat(initials);
    }

    private static IEnumerable<CustomerPaymentObligationListItem> ApplyStatus(
        IEnumerable<CustomerPaymentObligationListItem> query,
        PaymentObligationStatus? status) => status switch
        {
            PaymentObligationStatus exactStatus => query.Where(item => item.Status == exactStatus),
            _ => query
        };

    private static bool HasValidHeader(ObligationProjection item, long paid) =>
        item.TargetId > 0
        && !string.IsNullOrWhiteSpace(item.Reference)
        && item.OriginalAmountMinor > 0
        && paid >= 0
        && paid <= item.OriginalAmountMinor
        && (item.TargetType == CustomerPaymentObligationTargetType.Sale && item.PartyId is null
            || item.PartyId is > 0 && !string.IsNullOrWhiteSpace(item.PartyName));

    private static PaymentObligationStatus ToStatus(long paid, long remaining) =>
        remaining == 0
            ? PaymentObligationStatus.Settled
            : paid == 0
                ? PaymentObligationStatus.Outstanding
                : PaymentObligationStatus.PartiallyPaid;

    private sealed class ObligationProjection
    {
        public CustomerPaymentObligationTargetType TargetType { get; init; }
        public int TargetSort { get; init; }
        public long TargetId { get; init; }
        public required string Reference { get; init; }
        public DateOnly BusinessDate { get; init; }
        public long? PartyId { get; init; }
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
