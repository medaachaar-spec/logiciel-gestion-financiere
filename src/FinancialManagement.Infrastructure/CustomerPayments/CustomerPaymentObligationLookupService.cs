using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.CustomerPayments;

internal sealed class CustomerPaymentObligationLookupService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService) : ICustomerPaymentObligationLookupService
{
    public async Task<CustomerPaymentObligationLookupResult> SearchAsync(
        CustomerPaymentObligationLookupQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return Failed(query, CustomerPaymentObligationLookupFailureReason.NotAuthenticated);
        }

        var requiredAction = query.Purpose switch
        {
            CustomerPaymentObligationLookupPurpose.CreatePayment => PermissionAction.Create,
            CustomerPaymentObligationLookupPurpose.ApplyAdvance => PermissionAction.Modify,
            _ => (PermissionAction?)null
        };

        if (requiredAction is null)
        {
            return Failed(query, CustomerPaymentObligationLookupFailureReason.InvalidPurpose);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.CustomerPayments,
                requiredAction.Value,
                cancellationToken))
        {
            return Failed(query, CustomerPaymentObligationLookupFailureReason.Forbidden);
        }

        if (query.ClientId <= 0)
        {
            return Failed(query, CustomerPaymentObligationLookupFailureReason.InvalidClientId);
        }

        if (query.Limit is < 1 or > 200)
        {
            return Failed(query, CustomerPaymentObligationLookupFailureReason.InvalidLimit);
        }

        var clientExists = await dbContext.Clients
            .AsNoTracking()
            .AnyAsync(client => client.Id == query.ClientId, cancellationToken);
        if (!clientExists)
        {
            return Failed(query, CustomerPaymentObligationLookupFailureReason.ClientNotFound);
        }

        var searchText = query.SearchText?.Trim();

        var sales = dbContext.Sales
            .AsNoTracking()
            .Where(sale => sale.ClientId == query.ClientId);
        if (!string.IsNullOrEmpty(searchText))
        {
            sales = sales.Where(sale => sale.Reference.Contains(searchText));
        }

        var saleObligations = await sales
            .Select(sale => new ObligationProjection
            {
                TargetType = CustomerPaymentObligationTargetType.Sale,
                TargetId = sale.Id,
                Reference = sale.Reference,
                BusinessDate = sale.SaleDate,
                DueDate = sale.DueDate,
                OriginalAmountMinor = sale.TotalAmountMinor
            })
            .ToListAsync(cancellationToken);

        var receivables = dbContext.InitialCustomerReceivables
            .AsNoTracking()
            .Where(receivable => receivable.ClientId == query.ClientId);
        if (!string.IsNullOrEmpty(searchText))
        {
            receivables = receivables.Where(receivable => receivable.Reference.Contains(searchText));
        }

        var receivableObligations = await receivables
            .Select(receivable => new ObligationProjection
            {
                TargetType = CustomerPaymentObligationTargetType.InitialCustomerReceivable,
                TargetId = receivable.Id,
                Reference = receivable.Reference,
                BusinessDate = receivable.ReceivableDate,
                DueDate = receivable.DueDate,
                OriginalAmountMinor = receivable.InitialAmountMinor
            })
            .ToListAsync(cancellationToken);

        var obligations = saleObligations.Concat(receivableObligations).ToList();
        var integrity = await AllocationIntegrity.ReadCustomerAsync(
            dbContext, saleObligations.Select(item => item.TargetId).ToHashSet(),
            receivableObligations.Select(item => item.TargetId).ToHashSet(), null, cancellationToken);
        if (integrity is null)
            return Failed(query, CustomerPaymentObligationLookupFailureReason.DataIntegrityViolation);
        foreach (var obligation in obligations)
        {
            var totals = obligation.TargetType == CustomerPaymentObligationTargetType.Sale
                ? integrity.Documents : integrity.Initials;
            obligation.AllocatedAmountMinor = totals.GetValueOrDefault(obligation.TargetId);
        }

        if (obligations.Any(HasInvalidAggregate))
        {
            return Failed(query, CustomerPaymentObligationLookupFailureReason.DataIntegrityViolation);
        }

        var items = obligations
            .Where(obligation => obligation.AllocatedAmountMinor < obligation.OriginalAmountMinor)
            .OrderBy(obligation => obligation.BusinessDate)
            .ThenBy(obligation => obligation.TargetId)
            .ThenBy(obligation => obligation.TargetType)
            .Take(query.Limit)
            .Select(obligation => new CustomerPaymentObligationLookupItem(
                obligation.TargetType,
                obligation.TargetId,
                obligation.Reference,
                obligation.BusinessDate,
                obligation.DueDate,
                obligation.OriginalAmountMinor,
                obligation.OriginalAmountMinor - obligation.AllocatedAmountMinor))
            .ToList();

        return CustomerPaymentObligationLookupResult.Succeeded(query, items);
    }

    private static CustomerPaymentObligationLookupResult Failed(
        CustomerPaymentObligationLookupQuery query,
        CustomerPaymentObligationLookupFailureReason failureReason) =>
        CustomerPaymentObligationLookupResult.Failed(query, failureReason);

    private static bool HasInvalidAggregate(ObligationProjection obligation) =>
        obligation.OriginalAmountMinor <= 0
        || obligation.AllocatedAmountMinor < 0
        || obligation.AllocatedAmountMinor > obligation.OriginalAmountMinor;

    private sealed class ObligationProjection
    {
        public CustomerPaymentObligationTargetType TargetType { get; init; }

        public long TargetId { get; init; }

        public required string Reference { get; init; }

        public DateOnly BusinessDate { get; init; }

        public DateOnly? DueDate { get; init; }

        public long OriginalAmountMinor { get; init; }

        public long AllocatedAmountMinor { get; set; }
    }
}
