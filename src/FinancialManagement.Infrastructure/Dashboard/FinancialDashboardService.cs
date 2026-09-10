using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Application.Treasury;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Dashboard;

internal sealed class FinancialDashboardService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    ITreasuryService treasuryService) : IFinancialDashboardService, IFinancialReportsService
{
    Task<FinancialDashboardResult> IFinancialDashboardService.GetAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken) =>
        GetAsync(query, ResponsibleCapability.GlobalDashboard, cancellationToken);

    Task<FinancialDashboardResult> IFinancialReportsService.GetAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken) =>
        GetAsync(query, ResponsibleCapability.Reports, cancellationToken);

    private async Task<FinancialDashboardResult> GetAsync(
        FinancialDashboardQuery query,
        ResponsibleCapability requiredCapability,
        CancellationToken cancellationToken)
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
                requiredCapability))
        {
            return FinancialDashboardResult.Failed(
                query,
                FinancialDashboardFailureReason.Forbidden);
        }

        if (!Enum.IsDefined(query.Granularity)
            || query.StartDate is DateOnly startDate
            && query.EndDate is DateOnly endDate
            && startDate > endDate)
        {
            return FinancialDashboardResult.Failed(
                query,
                FinancialDashboardFailureReason.InvalidDateRange);
        }

        if (query.ComparisonStartDate.HasValue != query.ComparisonEndDate.HasValue
            || query.ComparisonStartDate is DateOnly comparisonStart
                && query.ComparisonEndDate is DateOnly comparisonEnd
                && comparisonStart > comparisonEnd)
        {
            return FinancialDashboardResult.Failed(
                query,
                FinancialDashboardFailureReason.InvalidDateRange);
        }

        // Balance our EF open-reference even if the caller already owns an open connection.
        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
            // The first SELECT establishes the WAL snapshot without reserving a writer lock.
            await using var transaction = connection.BeginTransaction(deferred: true);
            await using var attachment = await dbContext.Database.UseTransactionAsync(
                transaction, cancellationToken);
            var result = await ReadSnapshotAsync(query, cancellationToken);
            if (result.Success)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return result;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task<FinancialDashboardResult> ReadSnapshotAsync(
        FinancialDashboardQuery query,
        CancellationToken cancellationToken)
    {
        var selectedFlows = await ReadFlowSnapshotAsync(
            query.StartDate,
            query.EndDate,
            includeCashFlows: true,
            query.Granularity,
            cancellationToken);
        var comparisonFlows = query.ComparisonStartDate is DateOnly comparisonStart
            && query.ComparisonEndDate is DateOnly comparisonEnd
            ? await ReadFlowSnapshotAsync(
                comparisonStart,
                comparisonEnd,
                includeCashFlows: true,
                query.Granularity,
                cancellationToken)
            : FlowSnapshot.Empty;
        var customerReceivables = query.IncludeCurrentPositions
            ? await CalculateCustomerReceivablesAsync(cancellationToken)
            : OutstandingSummary.Empty;
        var supplierPayables = query.IncludeCurrentPositions
            ? await CalculateSupplierPayablesAsync(cancellationToken)
            : OutstandingSummary.Empty;
        var treasury = query.IncludeCurrentPositions
            ? await treasuryService.GetSummaryAsync(cancellationToken)
            : TreasurySummaryResult.Succeeded(
                false, null, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

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
        long comparisonResultMinor;
        checked
        {
            resultMinor = selectedFlows.SalesMinor
                - selectedFlows.PurchasesMinor
                - selectedFlows.ExpensesMinor;
            comparisonResultMinor = comparisonFlows.SalesMinor
                - comparisonFlows.PurchasesMinor
                - comparisonFlows.ExpensesMinor;
        }

        return FinancialDashboardResult.Succeeded(
            query,
            selectedFlows.SalesMinor,
            selectedFlows.PurchasesMinor,
            selectedFlows.CustomerCollectionsMinor,
            selectedFlows.SupplierPaymentsMinor,
            selectedFlows.ExpensesMinor,
            resultMinor,
            comparisonFlows.SalesMinor,
            comparisonFlows.PurchasesMinor,
            comparisonFlows.ExpensesMinor,
            comparisonResultMinor,
            comparisonFlows.CustomerCollectionsMinor,
            comparisonFlows.SupplierPaymentsMinor,
            customerReceivables.TotalMinor,
            supplierPayables.TotalMinor,
            treasury.CashBalanceMinor,
            treasury.BankBalanceMinor,
            treasury.TotalTreasuryMinor,
            treasury.IsInitialized,
            treasury.OpeningDate,
            BuildPoints(query, selectedFlows),
            customerReceivables.TopBalances,
            supplierPayables.TopBalances,
            customerReceivables.PartyCount,
            supplierPayables.PartyCount);
    }

    private async Task<FlowSnapshot> ReadFlowSnapshotAsync(
        DateOnly? startDate,
        DateOnly? endDate,
        bool includeCashFlows,
        FinancialDashboardGranularity granularity,
        CancellationToken cancellationToken)
    {
        var sales = await ReadSalesAsync(startDate, endDate, granularity, cancellationToken);
        var purchases = await ReadPurchasesAsync(startDate, endDate, granularity, cancellationToken);
        var expenses = await ReadExpensesAsync(startDate, endDate, granularity, cancellationToken);
        var customerCollections = includeCashFlows
            ? await ReadCustomerCollectionsAsync(startDate, endDate, granularity, cancellationToken)
            : [];
        var supplierPayments = includeCashFlows
            ? await ReadSupplierPaymentsAsync(startDate, endDate, granularity, cancellationToken)
            : [];

        return new FlowSnapshot(
            SumBuckets(sales),
            SumBuckets(purchases),
            SumBuckets(expenses),
            SumBuckets(customerCollections),
            SumBuckets(supplierPayments),
            sales,
            purchases,
            expenses,
            customerCollections,
            supplierPayments);
    }

    private async Task<List<BucketAmount>> ReadSalesAsync(
        DateOnly? startDate,
        DateOnly? endDate,
        FinancialDashboardGranularity granularity,
        CancellationToken cancellationToken)
    {
        var sales = dbContext.Sales.AsNoTracking();
        sales = ApplyDateFilter(sales, startDate, endDate, sale => sale.SaleDate);
        if (granularity == FinancialDashboardGranularity.Monthly)
        {
            var monthly = await sales
                .GroupBy(sale => new { sale.SaleDate.Year, sale.SaleDate.Month })
                .Select(group => new { group.Key.Year, group.Key.Month, AmountMinor = group.Sum(item => item.TotalAmountMinor) })
                .OrderBy(item => item.Year).ThenBy(item => item.Month)
                .ToListAsync(cancellationToken);
            return monthly.Select(item => new BucketAmount(
                new DateOnly(item.Year, item.Month, 1), item.AmountMinor)).ToList();
        }

        var daily = await sales
            .GroupBy(sale => sale.SaleDate)
            .Select(group => new { Date = group.Key, AmountMinor = group.Sum(item => item.TotalAmountMinor) })
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);
        return daily.Select(item => new BucketAmount(item.Date, item.AmountMinor)).ToList();
    }

    private async Task<List<BucketAmount>> ReadPurchasesAsync(
        DateOnly? startDate,
        DateOnly? endDate,
        FinancialDashboardGranularity granularity,
        CancellationToken cancellationToken)
    {
        var purchases = dbContext.Purchases.AsNoTracking();
        purchases = ApplyDateFilter(
            purchases,
            startDate,
            endDate,
            purchase => purchase.PurchaseDate);
        if (granularity == FinancialDashboardGranularity.Monthly)
        {
            var monthly = await purchases
                .GroupBy(purchase => new { purchase.PurchaseDate.Year, purchase.PurchaseDate.Month })
                .Select(group => new { group.Key.Year, group.Key.Month, AmountMinor = group.Sum(item => item.TotalAmountMinor) })
                .OrderBy(item => item.Year).ThenBy(item => item.Month)
                .ToListAsync(cancellationToken);
            return monthly.Select(item => new BucketAmount(
                new DateOnly(item.Year, item.Month, 1), item.AmountMinor)).ToList();
        }

        var daily = await purchases
            .GroupBy(purchase => purchase.PurchaseDate)
            .Select(group => new { Date = group.Key, AmountMinor = group.Sum(item => item.TotalAmountMinor) })
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);
        return daily.Select(item => new BucketAmount(item.Date, item.AmountMinor)).ToList();
    }

    private async Task<List<BucketAmount>> ReadCustomerCollectionsAsync(
        DateOnly? startDate,
        DateOnly? endDate,
        FinancialDashboardGranularity granularity,
        CancellationToken cancellationToken)
    {
        var payments = dbContext.CustomerPayments.AsNoTracking();
        payments = ApplyDateFilter(
            payments,
            startDate,
            endDate,
            payment => payment.PaymentDate);
        if (granularity == FinancialDashboardGranularity.Monthly)
        {
            var monthly = await payments
                .GroupBy(payment => new { payment.PaymentDate.Year, payment.PaymentDate.Month })
                .Select(group => new { group.Key.Year, group.Key.Month, AmountMinor = group.Sum(item => item.AmountMinor) })
                .OrderBy(item => item.Year).ThenBy(item => item.Month)
                .ToListAsync(cancellationToken);
            return monthly.Select(item => new BucketAmount(
                new DateOnly(item.Year, item.Month, 1), item.AmountMinor)).ToList();
        }

        var daily = await payments
            .GroupBy(payment => payment.PaymentDate)
            .Select(group => new { Date = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);
        return daily.Select(item => new BucketAmount(item.Date, item.AmountMinor)).ToList();
    }

    private async Task<List<BucketAmount>> ReadSupplierPaymentsAsync(
        DateOnly? startDate,
        DateOnly? endDate,
        FinancialDashboardGranularity granularity,
        CancellationToken cancellationToken)
    {
        var payments = dbContext.SupplierPayments.AsNoTracking();
        payments = ApplyDateFilter(
            payments,
            startDate,
            endDate,
            payment => payment.PaymentDate);
        if (granularity == FinancialDashboardGranularity.Monthly)
        {
            var monthly = await payments
                .GroupBy(payment => new { payment.PaymentDate.Year, payment.PaymentDate.Month })
                .Select(group => new { group.Key.Year, group.Key.Month, AmountMinor = group.Sum(item => item.AmountMinor) })
                .OrderBy(item => item.Year).ThenBy(item => item.Month)
                .ToListAsync(cancellationToken);
            return monthly.Select(item => new BucketAmount(
                new DateOnly(item.Year, item.Month, 1), item.AmountMinor)).ToList();
        }

        var daily = await payments
            .GroupBy(payment => payment.PaymentDate)
            .Select(group => new { Date = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);
        return daily.Select(item => new BucketAmount(item.Date, item.AmountMinor)).ToList();
    }

    private async Task<List<BucketAmount>> ReadExpensesAsync(
        DateOnly? startDate,
        DateOnly? endDate,
        FinancialDashboardGranularity granularity,
        CancellationToken cancellationToken)
    {
        var expenses = dbContext.Expenses.AsNoTracking();
        expenses = ApplyDateFilter(
            expenses,
            startDate,
            endDate,
            expense => expense.ExpenseDate);
        if (granularity == FinancialDashboardGranularity.Monthly)
        {
            var monthly = await expenses
                .GroupBy(expense => new { expense.ExpenseDate.Year, expense.ExpenseDate.Month })
                .Select(group => new { group.Key.Year, group.Key.Month, AmountMinor = group.Sum(item => item.AmountMinor) })
                .OrderBy(item => item.Year).ThenBy(item => item.Month)
                .ToListAsync(cancellationToken);
            return monthly.Select(item => new BucketAmount(
                new DateOnly(item.Year, item.Month, 1), item.AmountMinor)).ToList();
        }

        var daily = await expenses
            .GroupBy(expense => expense.ExpenseDate)
            .Select(group => new { Date = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .OrderBy(item => item.Date)
            .ToListAsync(cancellationToken);
        return daily.Select(item => new BucketAmount(item.Date, item.AmountMinor)).ToList();
    }

    private async Task<OutstandingSummary> CalculateCustomerReceivablesAsync(
        CancellationToken cancellationToken)
    {
        var sales = await dbContext.Sales
            .AsNoTracking()
            .Select(sale => new
            {
                sale.Id,
                sale.TotalAmountMinor,
                sale.ClientId,
                PartyName = sale.ClientId.HasValue
                    ? dbContext.Clients
                        .Where(client => client.Id == sale.ClientId.Value)
                        .Select(client => client.Name)
                        .Single()
                    : null
            })
            .ToListAsync(cancellationToken);
        var saleAllocations = await dbContext.CustomerPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.SaleId.HasValue)
            .GroupBy(allocation => allocation.SaleId!.Value)
            .Select(group => new { SaleId = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.SaleId, item => item.AmountMinor, cancellationToken);

        var initialReceivables = await dbContext.InitialCustomerReceivables
            .AsNoTracking()
            .Select(receivable => new
            {
                receivable.Id,
                receivable.InitialAmountMinor,
                receivable.ClientId,
                PartyName = dbContext.Clients
                    .Where(client => client.Id == receivable.ClientId)
                    .Select(client => client.Name)
                    .Single()
            })
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
        var balancesByParty = new Dictionary<long, PartyBalanceAccumulator>();
        foreach (var sale in sales)
        {
            var allocated = saleAllocations.GetValueOrDefault(sale.Id);
            var outstanding = CalculateOutstanding(
                "Sale",
                sale.Id,
                sale.TotalAmountMinor,
                allocated);
            total = checked(total + outstanding);
            if (sale.ClientId is long clientId)
            {
                AddPartyBalance(balancesByParty, clientId, sale.PartyName!, outstanding);
            }
        }

        foreach (var receivable in initialReceivables)
        {
            var allocated = initialAllocations.GetValueOrDefault(receivable.Id);
            var outstanding = CalculateOutstanding(
                "InitialCustomerReceivable",
                receivable.Id,
                receivable.InitialAmountMinor,
                allocated);
            total = checked(total + outstanding);
            AddPartyBalance(
                balancesByParty,
                receivable.ClientId,
                receivable.PartyName,
                outstanding);
        }

        return BuildOutstandingSummary(total, balancesByParty);
    }

    private async Task<OutstandingSummary> CalculateSupplierPayablesAsync(
        CancellationToken cancellationToken)
    {
        var purchases = await dbContext.Purchases
            .AsNoTracking()
            .Select(purchase => new
            {
                purchase.Id,
                purchase.TotalAmountMinor,
                purchase.SupplierId,
                PartyName = dbContext.Suppliers
                    .Where(supplier => supplier.Id == purchase.SupplierId)
                    .Select(supplier => supplier.Name)
                    .Single()
            })
            .ToListAsync(cancellationToken);
        var purchaseAllocations = await dbContext.SupplierPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.PurchaseId.HasValue)
            .GroupBy(allocation => allocation.PurchaseId!.Value)
            .Select(group => new { PurchaseId = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.PurchaseId, item => item.AmountMinor, cancellationToken);

        var initialPayables = await dbContext.InitialSupplierPayables
            .AsNoTracking()
            .Select(payable => new
            {
                payable.Id,
                payable.InitialAmountMinor,
                payable.SupplierId,
                PartyName = dbContext.Suppliers
                    .Where(supplier => supplier.Id == payable.SupplierId)
                    .Select(supplier => supplier.Name)
                    .Single()
            })
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
        var balancesByParty = new Dictionary<long, PartyBalanceAccumulator>();
        foreach (var purchase in purchases)
        {
            var allocated = purchaseAllocations.GetValueOrDefault(purchase.Id);
            var outstanding = CalculateOutstanding(
                "Purchase",
                purchase.Id,
                purchase.TotalAmountMinor,
                allocated);
            total = checked(total + outstanding);
            AddPartyBalance(
                balancesByParty,
                purchase.SupplierId,
                purchase.PartyName,
                outstanding);
        }

        foreach (var payable in initialPayables)
        {
            var allocated = initialAllocations.GetValueOrDefault(payable.Id);
            var outstanding = CalculateOutstanding(
                "InitialSupplierPayable",
                payable.Id,
                payable.InitialAmountMinor,
                allocated);
            total = checked(total + outstanding);
            AddPartyBalance(
                balancesByParty,
                payable.SupplierId,
                payable.PartyName,
                outstanding);
        }

        return BuildOutstandingSummary(total, balancesByParty);
    }

    private static IReadOnlyList<FinancialDashboardDailyPoint> BuildPoints(
        FinancialDashboardQuery query,
        FlowSnapshot flows)
    {
        var sales = flows.Sales.ToDictionary(item => item.Date, item => item.AmountMinor);
        var purchases = flows.Purchases.ToDictionary(item => item.Date, item => item.AmountMinor);
        var expenses = flows.Expenses.ToDictionary(item => item.Date, item => item.AmountMinor);
        var collections = flows.CustomerCollections.ToDictionary(item => item.Date, item => item.AmountMinor);
        var payments = flows.SupplierPayments.ToDictionary(item => item.Date, item => item.AmountMinor);

        IEnumerable<DateOnly> dates;
        if (query.StartDate is DateOnly start && query.EndDate is DateOnly end)
        {
            dates = query.Granularity == FinancialDashboardGranularity.Monthly
                ? EnumerateMonths(start, end)
                : Enumerable.Range(0, end.DayNumber - start.DayNumber + 1)
                    .Select(offset => start.AddDays(offset));
        }
        else
        {
            dates = sales.Keys
                .Concat(purchases.Keys)
                .Concat(expenses.Keys)
                .Concat(collections.Keys)
                .Concat(payments.Keys)
                .Distinct()
                .OrderBy(date => date);
        }

        return dates.Select(date => new FinancialDashboardDailyPoint(
                date,
                sales.GetValueOrDefault(date),
                purchases.GetValueOrDefault(date),
                expenses.GetValueOrDefault(date),
                collections.GetValueOrDefault(date),
                payments.GetValueOrDefault(date)))
            .ToArray();
    }

    private static IEnumerable<DateOnly> EnumerateMonths(DateOnly start, DateOnly end)
    {
        var current = new DateOnly(start.Year, start.Month, 1);
        var last = new DateOnly(end.Year, end.Month, 1);
        while (current <= last)
        {
            yield return current;
            current = current.AddMonths(1);
        }
    }

    private static long SumBuckets(IEnumerable<BucketAmount> values)
    {
        long total = 0;
        foreach (var value in values)
        {
            total = checked(total + value.AmountMinor);
        }

        return total;
    }

    private static void AddPartyBalance(
        IDictionary<long, PartyBalanceAccumulator> balances,
        long partyId,
        string partyName,
        long outstandingMinor)
    {
        if (!balances.TryGetValue(partyId, out var balance))
        {
            balances.Add(partyId, new PartyBalanceAccumulator(partyName, outstandingMinor));
            return;
        }

        balance.OutstandingMinor = checked(balance.OutstandingMinor + outstandingMinor);
    }

    private static OutstandingSummary BuildOutstandingSummary(
        long totalMinor,
        IReadOnlyDictionary<long, PartyBalanceAccumulator> balances)
    {
        var positiveBalances = balances
            .Where(item => item.Value.OutstandingMinor > 0)
            .OrderByDescending(item => item.Value.OutstandingMinor)
            .ThenBy(item => item.Value.PartyName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.Key)
            .ToArray();
        var topBalances = positiveBalances
            .Take(5)
            .Select(item => new FinancialDashboardPartyBalance(
                item.Key,
                item.Value.PartyName,
                item.Value.OutstandingMinor))
            .ToArray();
        return new OutstandingSummary(totalMinor, positiveBalances.Length, topBalances);
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

    private sealed record BucketAmount(DateOnly Date, long AmountMinor);

    private sealed record FlowSnapshot(
        long SalesMinor,
        long PurchasesMinor,
        long ExpensesMinor,
        long CustomerCollectionsMinor,
        long SupplierPaymentsMinor,
        IReadOnlyList<BucketAmount> Sales,
        IReadOnlyList<BucketAmount> Purchases,
        IReadOnlyList<BucketAmount> Expenses,
        IReadOnlyList<BucketAmount> CustomerCollections,
        IReadOnlyList<BucketAmount> SupplierPayments)
    {
        public static FlowSnapshot Empty { get; } = new(
            0,
            0,
            0,
            0,
            0,
            Array.Empty<BucketAmount>(),
            Array.Empty<BucketAmount>(),
            Array.Empty<BucketAmount>(),
            Array.Empty<BucketAmount>(),
            Array.Empty<BucketAmount>());
    }

    private sealed class PartyBalanceAccumulator(string partyName, long outstandingMinor)
    {
        public string PartyName { get; } = partyName;

        public long OutstandingMinor { get; set; } = outstandingMinor;
    }

    private sealed record OutstandingSummary(
        long TotalMinor,
        int PartyCount,
        IReadOnlyList<FinancialDashboardPartyBalance> TopBalances)
    {
        public static OutstandingSummary Empty { get; } =
            new(0, 0, Array.Empty<FinancialDashboardPartyBalance>());
    }
}
