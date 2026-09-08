using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.SupplierPayments;

internal sealed class SupplierPaymentObligationLookupService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService) : ISupplierPaymentObligationLookupService
{
    public async Task<SupplierPaymentObligationLookupResult> SearchAsync(
        SupplierPaymentObligationLookupQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return Failed(query, SupplierPaymentObligationLookupFailureReason.NotAuthenticated);
        }

        var requiredAction = query.Purpose switch
        {
            SupplierPaymentObligationLookupPurpose.CreatePayment => PermissionAction.Create,
            SupplierPaymentObligationLookupPurpose.ApplyAdvance => PermissionAction.Modify,
            _ => (PermissionAction?)null
        };

        if (requiredAction is null)
        {
            return Failed(query, SupplierPaymentObligationLookupFailureReason.InvalidPurpose);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.SupplierPayments,
                requiredAction.Value,
                cancellationToken))
        {
            return Failed(query, SupplierPaymentObligationLookupFailureReason.Forbidden);
        }

        if (query.SupplierId <= 0)
        {
            return Failed(query, SupplierPaymentObligationLookupFailureReason.InvalidSupplierId);
        }

        if (query.Limit is < 1 or > 200)
        {
            return Failed(query, SupplierPaymentObligationLookupFailureReason.InvalidLimit);
        }

        var supplierExists = await dbContext.Suppliers
            .AsNoTracking()
            .AnyAsync(supplier => supplier.Id == query.SupplierId, cancellationToken);
        if (!supplierExists)
        {
            return Failed(query, SupplierPaymentObligationLookupFailureReason.SupplierNotFound);
        }

        var searchText = query.SearchText?.Trim();

        var purchases = dbContext.Purchases
            .AsNoTracking()
            .Where(purchase => purchase.SupplierId == query.SupplierId);
        if (!string.IsNullOrEmpty(searchText))
        {
            purchases = purchases.Where(purchase => purchase.Reference.Contains(searchText));
        }

        var purchaseObligations = await purchases
            .Select(purchase => new ObligationProjection
            {
                TargetType = SupplierPaymentObligationTargetType.Purchase,
                TargetId = purchase.Id,
                Reference = purchase.Reference,
                BusinessDate = purchase.PurchaseDate,
                DueDate = purchase.DueDate,
                OriginalAmountMinor = purchase.TotalAmountMinor
            })
            .ToListAsync(cancellationToken);

        var payables = dbContext.InitialSupplierPayables
            .AsNoTracking()
            .Where(payable => payable.SupplierId == query.SupplierId);
        if (!string.IsNullOrEmpty(searchText))
        {
            payables = payables.Where(payable => payable.Reference.Contains(searchText));
        }

        var payableObligations = await payables
            .Select(payable => new ObligationProjection
            {
                TargetType = SupplierPaymentObligationTargetType.InitialSupplierPayable,
                TargetId = payable.Id,
                Reference = payable.Reference,
                BusinessDate = payable.PayableDate,
                DueDate = payable.DueDate,
                OriginalAmountMinor = payable.InitialAmountMinor
            })
            .ToListAsync(cancellationToken);

        var obligations = purchaseObligations.Concat(payableObligations).ToList();
        var integrity = await AllocationIntegrity.ReadSupplierAsync(
            dbContext, purchaseObligations.Select(item => item.TargetId).ToHashSet(),
            payableObligations.Select(item => item.TargetId).ToHashSet(), null, cancellationToken);
        if (integrity is null)
            return Failed(query, SupplierPaymentObligationLookupFailureReason.DataIntegrityViolation);
        foreach (var obligation in obligations)
        {
            var totals = obligation.TargetType == SupplierPaymentObligationTargetType.Purchase
                ? integrity.Documents : integrity.Initials;
            obligation.AllocatedAmountMinor = totals.GetValueOrDefault(obligation.TargetId);
        }

        if (obligations.Any(HasInvalidAggregate))
        {
            return Failed(query, SupplierPaymentObligationLookupFailureReason.DataIntegrityViolation);
        }

        var items = obligations
            .Where(obligation => obligation.AllocatedAmountMinor < obligation.OriginalAmountMinor)
            .OrderBy(obligation => obligation.BusinessDate)
            .ThenBy(obligation => obligation.TargetId)
            .ThenBy(obligation => obligation.TargetType)
            .Take(query.Limit)
            .Select(obligation => new SupplierPaymentObligationLookupItem(
                obligation.TargetType,
                obligation.TargetId,
                obligation.Reference,
                obligation.BusinessDate,
                obligation.DueDate,
                obligation.OriginalAmountMinor,
                obligation.OriginalAmountMinor - obligation.AllocatedAmountMinor))
            .ToList();

        return SupplierPaymentObligationLookupResult.Succeeded(query, items);
    }

    private static SupplierPaymentObligationLookupResult Failed(
        SupplierPaymentObligationLookupQuery query,
        SupplierPaymentObligationLookupFailureReason failureReason) =>
        SupplierPaymentObligationLookupResult.Failed(query, failureReason);

    private static bool HasInvalidAggregate(ObligationProjection obligation) =>
        obligation.OriginalAmountMinor <= 0
        || obligation.AllocatedAmountMinor < 0
        || obligation.AllocatedAmountMinor > obligation.OriginalAmountMinor;

    private sealed class ObligationProjection
    {
        public SupplierPaymentObligationTargetType TargetType { get; init; }

        public long TargetId { get; init; }

        public required string Reference { get; init; }

        public DateOnly BusinessDate { get; init; }

        public DateOnly? DueDate { get; init; }

        public long OriginalAmountMinor { get; init; }

        public long AllocatedAmountMinor { get; set; }
    }
}
