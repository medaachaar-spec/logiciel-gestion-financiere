using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Application.Treasury;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Dashboard;

internal sealed class FinancialDashboardService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    ITreasuryService treasuryService) : IFinancialDashboardService
{
    public async Task<FinancialDashboardResult> GetAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return FinancialDashboardResult.Failed(
                query,
                FinancialDashboardFailureReason.NotAuthenticated);
        }

        if (!authorizationService.CanAccessResponsibleCapability(
                ResponsibleCapability.GlobalDashboard))
        {
            return FinancialDashboardResult.Failed(
                query,
                FinancialDashboardFailureReason.Forbidden);
        }

        if (query.StartDate is DateOnly startDate
            && query.EndDate is DateOnly endDate
            && startDate > endDate)
        {
            return FinancialDashboardResult.Failed(
                query,
                FinancialDashboardFailureReason.InvalidDateRange);
        }

        var totalSalesMinor = await SumSalesAsync(query, cancellationToken);
        var totalPurchasesMinor = await SumPurchasesAsync(query, cancellationToken);
        var customerCollectionsMinor = await SumCustomerCollectionsAsync(query, cancellationToken);
        var supplierPaymentsMinor = await SumSupplierPaymentsAsync(query, cancellationToken);
        var expensesMinor = await SumExpensesAsync(query, cancellationToken);
        var currentCustomerReceivablesMinor =
            await CalculateCustomerReceivablesAsync(cancellationToken);
        var currentSupplierPayablesMinor =
            await CalculateSupplierPayablesAsync(cancellationToken);
        var treasury = await treasuryService.GetSummaryAsync(cancellationToken);

        if (!treasury.Success)
        {
            var failureReason = treasury.FailureReason switch
            {
                TreasurySummaryFailureReason.NotAuthenticated =>
                    FinancialDashboardFailureReason.NotAuthenticated,
                TreasurySummaryFailureReason.Forbidden =>
                    FinancialDashboardFailureReason.Forbidden,
                _ => throw new InvalidOperationException(
                    "The treasury summary returned an unexpected failure.")
            };

            return FinancialDashboardResult.Failed(query, failureReason);
        }

        long resultMinor;
        checked
        {
            resultMinor = totalSalesMinor - totalPurchasesMinor - expensesMinor;
        }

        return FinancialDashboardResult.Succeeded(
            query,
            totalSalesMinor,
            totalPurchasesMinor,
            customerCollectionsMinor,
            supplierPaymentsMinor,
            expensesMinor,
            resultMinor,
            currentCustomerReceivablesMinor,
            currentSupplierPayablesMinor,
            treasury.CashBalanceMinor,
            treasury.BankBalanceMinor,
            treasury.TotalTreasuryMinor,
            treasury.IsInitialized,
            treasury.OpeningDate);
    }

    private Task<long> SumSalesAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken)
    {
        var sales = dbContext.Sales.AsNoTracking();
        sales = ApplyDateFilter(sales, query.StartDate, query.EndDate, sale => sale.SaleDate);
        return sales.SumAsync(sale => sale.TotalAmountMinor, cancellationToken);
    }

    private Task<long> SumPurchasesAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken)
    {
        var purchases = dbContext.Purchases.AsNoTracking();
        purchases = ApplyDateFilter(
            purchases,
            query.StartDate,
            query.EndDate,
            purchase => purchase.PurchaseDate);
        return purchases.SumAsync(purchase => purchase.TotalAmountMinor, cancellationToken);
    }

    private Task<long> SumCustomerCollectionsAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken)
    {
        var payments = dbContext.CustomerPayments.AsNoTracking();
        payments = ApplyDateFilter(
            payments,
            query.StartDate,
            query.EndDate,
            payment => payment.PaymentDate);
        return payments.SumAsync(payment => payment.AmountMinor, cancellationToken);
    }

    private Task<long> SumSupplierPaymentsAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken)
    {
        var payments = dbContext.SupplierPayments.AsNoTracking();
        payments = ApplyDateFilter(
            payments,
            query.StartDate,
            query.EndDate,
            payment => payment.PaymentDate);
        return payments.SumAsync(payment => payment.AmountMinor, cancellationToken);
    }

    private Task<long> SumExpensesAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken)
    {
        var expenses = dbContext.Expenses.AsNoTracking();
        expenses = ApplyDateFilter(
            expenses,
            query.StartDate,
            query.EndDate,
            expense => expense.ExpenseDate);
        return expenses.SumAsync(expense => expense.AmountMinor, cancellationToken);
    }

    private async Task<long> CalculateCustomerReceivablesAsync(
        CancellationToken cancellationToken)
    {
        var sales = await dbContext.Sales
            .AsNoTracking()
            .Select(sale => new { sale.Id, sale.TotalAmountMinor })
            .ToListAsync(cancellationToken);
        var saleAllocations = await dbContext.CustomerPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.SaleId.HasValue)
            .GroupBy(allocation => allocation.SaleId!.Value)
            .Select(group => new { SaleId = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.SaleId, item => item.AmountMinor, cancellationToken);

        var initialReceivables = await dbContext.InitialCustomerReceivables
            .AsNoTracking()
            .Select(receivable => new { receivable.Id, receivable.InitialAmountMinor })
            .ToListAsync(cancellationToken);
        var initialAllocations = await dbContext.CustomerPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.InitialCustomerReceivableId.HasValue)
            .GroupBy(allocation => allocation.InitialCustomerReceivableId!.Value)
            .Select(group => new
            {
                InitialCustomerReceivableId = group.Key,
                AmountMinor = group.Sum(item => item.AmountMinor)
            })
            .ToDictionaryAsync(
                item => item.InitialCustomerReceivableId,
                item => item.AmountMinor,
                cancellationToken);

        long total = 0;
        foreach (var sale in sales)
        {
            var allocated = saleAllocations.GetValueOrDefault(sale.Id);
            total = checked(total + CalculateOutstanding(
                "Sale",
                sale.Id,
                sale.TotalAmountMinor,
                allocated));
        }

        foreach (var receivable in initialReceivables)
        {
            var allocated = initialAllocations.GetValueOrDefault(receivable.Id);
            total = checked(total + CalculateOutstanding(
                "InitialCustomerReceivable",
                receivable.Id,
                receivable.InitialAmountMinor,
                allocated));
        }

        return total;
    }

    private async Task<long> CalculateSupplierPayablesAsync(
        CancellationToken cancellationToken)
    {
        var purchases = await dbContext.Purchases
            .AsNoTracking()
            .Select(purchase => new { purchase.Id, purchase.TotalAmountMinor })
            .ToListAsync(cancellationToken);
        var purchaseAllocations = await dbContext.SupplierPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.PurchaseId.HasValue)
            .GroupBy(allocation => allocation.PurchaseId!.Value)
            .Select(group => new { PurchaseId = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.PurchaseId, item => item.AmountMinor, cancellationToken);

        var initialPayables = await dbContext.InitialSupplierPayables
            .AsNoTracking()
            .Select(payable => new { payable.Id, payable.InitialAmountMinor })
            .ToListAsync(cancellationToken);
        var initialAllocations = await dbContext.SupplierPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.InitialSupplierPayableId.HasValue)
            .GroupBy(allocation => allocation.InitialSupplierPayableId!.Value)
            .Select(group => new
            {
                InitialSupplierPayableId = group.Key,
                AmountMinor = group.Sum(item => item.AmountMinor)
            })
            .ToDictionaryAsync(
                item => item.InitialSupplierPayableId,
                item => item.AmountMinor,
                cancellationToken);

        long total = 0;
        foreach (var purchase in purchases)
        {
            var allocated = purchaseAllocations.GetValueOrDefault(purchase.Id);
            total = checked(total + CalculateOutstanding(
                "Purchase",
                purchase.Id,
                purchase.TotalAmountMinor,
                allocated));
        }

        foreach (var payable in initialPayables)
        {
            var allocated = initialAllocations.GetValueOrDefault(payable.Id);
            total = checked(total + CalculateOutstanding(
                "InitialSupplierPayable",
                payable.Id,
                payable.InitialAmountMinor,
                allocated));
        }

        return total;
    }

    private static long CalculateOutstanding(
        string obligationType,
        long obligationId,
        long amountMinor,
        long allocatedMinor)
    {
        if (allocatedMinor > amountMinor)
        {
            throw new InvalidOperationException(
                $"{obligationType} {obligationId} is overallocated: " +
                $"{allocatedMinor} exceeds {amountMinor}.");
        }

        return checked(amountMinor - allocatedMinor);
    }

    private static IQueryable<T> ApplyDateFilter<T>(
        IQueryable<T> source,
        DateOnly? startDate,
        DateOnly? endDate,
        System.Linq.Expressions.Expression<Func<T, DateOnly>> dateSelector)
    {
        if (startDate is DateOnly start)
        {
            source = source.Where(BuildComparison(dateSelector, start, greaterThanOrEqual: true));
        }

        if (endDate is DateOnly end)
        {
            source = source.Where(BuildComparison(dateSelector, end, greaterThanOrEqual: false));
        }

        return source;
    }

    private static System.Linq.Expressions.Expression<Func<T, bool>> BuildComparison<T>(
        System.Linq.Expressions.Expression<Func<T, DateOnly>> selector,
        DateOnly value,
        bool greaterThanOrEqual)
    {
        var parameter = selector.Parameters[0];
        var comparison = greaterThanOrEqual
            ? System.Linq.Expressions.Expression.GreaterThanOrEqual(
                selector.Body,
                System.Linq.Expressions.Expression.Constant(value))
            : System.Linq.Expressions.Expression.LessThanOrEqual(
                selector.Body,
                System.Linq.Expressions.Expression.Constant(value));
        return System.Linq.Expressions.Expression.Lambda<Func<T, bool>>(comparison, parameter);
    }
}
