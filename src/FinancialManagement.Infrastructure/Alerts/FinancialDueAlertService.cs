using FinancialManagement.Application.Alerts;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Alerts;

internal sealed class FinancialDueAlertService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    TimeProvider timeProvider) : IFinancialDueAlertService
{
    public async Task<FinancialDueAlertsResult> GetAlertsAsync(
        FinancialDueAlertsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return FinancialDueAlertsResult.Failed(
                FinancialDueAlertsFailureReason.NotAuthenticated);
        }

        if (!authorizationService.CanAccessResponsibleCapability(
                ResponsibleCapability.GlobalDashboard))
        {
            return FinancialDueAlertsResult.Failed(
                FinancialDueAlertsFailureReason.Forbidden);
        }

        var currentInstant = timeProvider.GetUtcNow();
        var localInstant = TimeZoneInfo.ConvertTime(currentInstant, timeProvider.LocalTimeZone);
        var currentDate = DateOnly.FromDateTime(localInstant.DateTime);
        var lastVisibleDueDate = currentDate.AddDays(5);

        var customerAlerts = await LoadCustomerAlertsAsync(
            currentDate,
            lastVisibleDueDate,
            cancellationToken);
        var supplierAlerts = await LoadSupplierAlertsAsync(
            currentDate,
            lastVisibleDueDate,
            cancellationToken);

        return FinancialDueAlertsResult.Succeeded(
            currentDate,
            customerAlerts,
            supplierAlerts);
    }

    private async Task<IReadOnlyList<FinancialDueAlert>> LoadCustomerAlertsAsync(
        DateOnly currentDate,
        DateOnly lastVisibleDueDate,
        CancellationToken cancellationToken)
    {
        var sales = await (
                from sale in dbContext.Sales.AsNoTracking()
                join client in dbContext.Clients.AsNoTracking()
                    on sale.ClientId equals client.Id into clients
                from client in clients.DefaultIfEmpty()
                select new
                {
                    sale.Id,
                    sale.Reference,
                    sale.ClientId,
                    PartyName = client == null ? null : client.Name,
                    sale.DueDate,
                    AmountMinor = sale.TotalAmountMinor
                })
            .ToListAsync(cancellationToken);
        var saleAllocations = await dbContext.CustomerPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.SaleId.HasValue)
            .GroupBy(allocation => allocation.SaleId!.Value)
            .Select(group => new { SourceId = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.SourceId, item => item.AmountMinor, cancellationToken);

        var initialReceivables = await (
                from receivable in dbContext.InitialCustomerReceivables.AsNoTracking()
                join client in dbContext.Clients.AsNoTracking()
                    on receivable.ClientId equals client.Id
                select new
                {
                    receivable.Id,
                    receivable.Reference,
                    PartyId = receivable.ClientId,
                    PartyName = client.Name,
                    receivable.DueDate,
                    AmountMinor = receivable.InitialAmountMinor
                })
            .ToListAsync(cancellationToken);
        var initialAllocations = await dbContext.CustomerPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.InitialCustomerReceivableId.HasValue)
            .GroupBy(allocation => allocation.InitialCustomerReceivableId!.Value)
            .Select(group => new { SourceId = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.SourceId, item => item.AmountMinor, cancellationToken);

        var alerts = new List<FinancialDueAlert>();
        foreach (var sale in sales)
        {
            var outstanding = CalculateOutstanding(
                FinancialDueAlertSourceType.Sale,
                sale.Id,
                sale.AmountMinor,
                saleAllocations.GetValueOrDefault(sale.Id));
            if (outstanding == 0 || sale.DueDate is not DateOnly dueDate)
            {
                continue;
            }

            if (sale.ClientId is not long partyId || sale.PartyName is null)
            {
                throw new InvalidOperationException(
                    $"Sale {sale.Id} has an outstanding due amount but no valid client.");
            }

            AddAlert(
                alerts,
                FinancialDueAlertSourceType.Sale,
                FinancialDueAlertSide.CustomerReceivable,
                sale.Id,
                sale.Reference,
                partyId,
                sale.PartyName,
                dueDate,
                outstanding,
                sale.AmountMinor,
                currentDate,
                lastVisibleDueDate);
        }

        foreach (var receivable in initialReceivables)
        {
            var outstanding = CalculateOutstanding(
                FinancialDueAlertSourceType.InitialCustomerReceivable,
                receivable.Id,
                receivable.AmountMinor,
                initialAllocations.GetValueOrDefault(receivable.Id));
            if (outstanding == 0 || receivable.DueDate is not DateOnly dueDate)
            {
                continue;
            }

            AddAlert(
                alerts,
                FinancialDueAlertSourceType.InitialCustomerReceivable,
                FinancialDueAlertSide.CustomerReceivable,
                receivable.Id,
                receivable.Reference,
                receivable.PartyId,
                receivable.PartyName,
                dueDate,
                outstanding,
                receivable.AmountMinor,
                currentDate,
                lastVisibleDueDate);
        }

        return alerts
            .OrderBy(alert => alert.DueDate)
            .ThenBy(alert => alert.Reference, StringComparer.Ordinal)
            .ToArray();
    }

    private async Task<IReadOnlyList<FinancialDueAlert>> LoadSupplierAlertsAsync(
        DateOnly currentDate,
        DateOnly lastVisibleDueDate,
        CancellationToken cancellationToken)
    {
        var purchases = await (
                from purchase in dbContext.Purchases.AsNoTracking()
                join supplier in dbContext.Suppliers.AsNoTracking()
                    on purchase.SupplierId equals supplier.Id
                select new
                {
                    purchase.Id,
                    purchase.Reference,
                    PartyId = purchase.SupplierId,
                    PartyName = supplier.Name,
                    purchase.DueDate,
                    AmountMinor = purchase.TotalAmountMinor
                })
            .ToListAsync(cancellationToken);
        var purchaseAllocations = await dbContext.SupplierPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.PurchaseId.HasValue)
            .GroupBy(allocation => allocation.PurchaseId!.Value)
            .Select(group => new { SourceId = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.SourceId, item => item.AmountMinor, cancellationToken);

        var initialPayables = await (
                from payable in dbContext.InitialSupplierPayables.AsNoTracking()
                join supplier in dbContext.Suppliers.AsNoTracking()
                    on payable.SupplierId equals supplier.Id
                select new
                {
                    payable.Id,
                    payable.Reference,
                    PartyId = payable.SupplierId,
                    PartyName = supplier.Name,
                    payable.DueDate,
                    AmountMinor = payable.InitialAmountMinor
                })
            .ToListAsync(cancellationToken);
        var initialAllocations = await dbContext.SupplierPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.InitialSupplierPayableId.HasValue)
            .GroupBy(allocation => allocation.InitialSupplierPayableId!.Value)
            .Select(group => new { SourceId = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.SourceId, item => item.AmountMinor, cancellationToken);

        var alerts = new List<FinancialDueAlert>();
        foreach (var purchase in purchases)
        {
            var outstanding = CalculateOutstanding(
                FinancialDueAlertSourceType.Purchase,
                purchase.Id,
                purchase.AmountMinor,
                purchaseAllocations.GetValueOrDefault(purchase.Id));
            if (outstanding == 0 || purchase.DueDate is not DateOnly dueDate)
            {
                continue;
            }

            AddAlert(
                alerts,
                FinancialDueAlertSourceType.Purchase,
                FinancialDueAlertSide.SupplierPayable,
                purchase.Id,
                purchase.Reference,
                purchase.PartyId,
                purchase.PartyName,
                dueDate,
                outstanding,
                purchase.AmountMinor,
                currentDate,
                lastVisibleDueDate);
        }

        foreach (var payable in initialPayables)
        {
            var outstanding = CalculateOutstanding(
                FinancialDueAlertSourceType.InitialSupplierPayable,
                payable.Id,
                payable.AmountMinor,
                initialAllocations.GetValueOrDefault(payable.Id));
            if (outstanding == 0 || payable.DueDate is not DateOnly dueDate)
            {
                continue;
            }

            AddAlert(
                alerts,
                FinancialDueAlertSourceType.InitialSupplierPayable,
                FinancialDueAlertSide.SupplierPayable,
                payable.Id,
                payable.Reference,
                payable.PartyId,
                payable.PartyName,
                dueDate,
                outstanding,
                payable.AmountMinor,
                currentDate,
                lastVisibleDueDate);
        }

        return alerts
            .OrderBy(alert => alert.DueDate)
            .ThenBy(alert => alert.Reference, StringComparer.Ordinal)
            .ToArray();
    }

    private static long CalculateOutstanding(
        FinancialDueAlertSourceType sourceType,
        long sourceId,
        long amountMinor,
        long allocatedMinor)
    {
        if (allocatedMinor > amountMinor)
        {
            throw new InvalidOperationException(
                $"{sourceType} {sourceId} is overallocated: " +
                $"{allocatedMinor} exceeds {amountMinor}.");
        }

        return checked(amountMinor - allocatedMinor);
    }

    private static void AddAlert(
        ICollection<FinancialDueAlert> alerts,
        FinancialDueAlertSourceType sourceType,
        FinancialDueAlertSide side,
        long sourceId,
        string reference,
        long partyId,
        string partyName,
        DateOnly dueDate,
        long outstandingMinor,
        long originalAmountMinor,
        DateOnly currentDate,
        DateOnly lastVisibleDueDate)
    {
        if (dueDate > lastVisibleDueDate)
        {
            return;
        }

        var daysUntilDue = dueDate.DayNumber - currentDate.DayNumber;
        var status = daysUntilDue switch
        {
            > 0 => FinancialDueAlertStatus.Upcoming,
            0 => FinancialDueAlertStatus.DueToday,
            _ => FinancialDueAlertStatus.Overdue
        };
        alerts.Add(new FinancialDueAlert(
            sourceType,
            side,
            sourceId,
            reference,
            partyId,
            partyName,
            dueDate,
            outstandingMinor,
            daysUntilDue,
            status,
            originalAmountMinor));
    }
}
