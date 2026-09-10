using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure;

// Reads the current position for a bounded party page. AllocationIntegrity is
// deliberately reused so list balances follow the same explicit-allocation and
// over-allocation rules as obligation reads and advance application.
internal static class CurrentPartyBalanceReader
{
    public static async Task<IReadOnlyDictionary<long, long>?> ReadCustomerAsync(
        FinancialManagementDbContext dbContext,
        IReadOnlyCollection<long> clientIds,
        CancellationToken cancellationToken)
    {
        var partyIds = clientIds.ToHashSet();
        if (partyIds.Count == 0)
        {
            return new Dictionary<long, long>();
        }

        var sales = await dbContext.Sales
            .AsNoTracking()
            .Where(sale => sale.ClientId.HasValue && partyIds.Contains(sale.ClientId.Value))
            .Select(sale => new Obligation(sale.Id, sale.ClientId!.Value, sale.TotalAmountMinor))
            .ToListAsync(cancellationToken);
        var initialReceivables = await dbContext.InitialCustomerReceivables
            .AsNoTracking()
            .Where(receivable => partyIds.Contains(receivable.ClientId))
            .Select(receivable => new Obligation(
                receivable.Id,
                receivable.ClientId,
                receivable.InitialAmountMinor))
            .ToListAsync(cancellationToken);

        var integrity = await AllocationIntegrity.ReadCustomerAsync(
            dbContext,
            sales.Select(item => item.Id).ToHashSet(),
            initialReceivables.Select(item => item.Id).ToHashSet(),
            null,
            cancellationToken);
        return integrity is null
            ? null
            : BuildBalances(
                partyIds,
                sales,
                initialReceivables,
                integrity.Documents,
                integrity.Initials);
    }

    public static async Task<IReadOnlyDictionary<long, long>?> ReadSupplierAsync(
        FinancialManagementDbContext dbContext,
        IReadOnlyCollection<long> supplierIds,
        CancellationToken cancellationToken)
    {
        var partyIds = supplierIds.ToHashSet();
        if (partyIds.Count == 0)
        {
            return new Dictionary<long, long>();
        }

        var purchases = await dbContext.Purchases
            .AsNoTracking()
            .Where(purchase => partyIds.Contains(purchase.SupplierId))
            .Select(purchase => new Obligation(
                purchase.Id,
                purchase.SupplierId,
                purchase.TotalAmountMinor))
            .ToListAsync(cancellationToken);
        var initialPayables = await dbContext.InitialSupplierPayables
            .AsNoTracking()
            .Where(payable => partyIds.Contains(payable.SupplierId))
            .Select(payable => new Obligation(
                payable.Id,
                payable.SupplierId,
                payable.InitialAmountMinor))
            .ToListAsync(cancellationToken);

        var integrity = await AllocationIntegrity.ReadSupplierAsync(
            dbContext,
            purchases.Select(item => item.Id).ToHashSet(),
            initialPayables.Select(item => item.Id).ToHashSet(),
            null,
            cancellationToken);
        return integrity is null
            ? null
            : BuildBalances(
                partyIds,
                purchases,
                initialPayables,
                integrity.Documents,
                integrity.Initials);
    }

    private static IReadOnlyDictionary<long, long>? BuildBalances(
        IReadOnlyCollection<long> partyIds,
        IReadOnlyCollection<Obligation> documents,
        IReadOnlyCollection<Obligation> initials,
        IReadOnlyDictionary<long, long> documentAllocations,
        IReadOnlyDictionary<long, long> initialAllocations)
    {
        var balances = partyIds.ToDictionary(partyId => partyId, _ => 0L);
        try
        {
            foreach (var document in documents)
            {
                if (!TryAddOutstanding(balances, document, documentAllocations))
                {
                    return null;
                }
            }

            foreach (var initial in initials)
            {
                if (!TryAddOutstanding(balances, initial, initialAllocations))
                {
                    return null;
                }
            }
        }
        catch (OverflowException)
        {
            return null;
        }

        return balances;
    }

    private static bool TryAddOutstanding(
        IDictionary<long, long> balances,
        Obligation obligation,
        IReadOnlyDictionary<long, long> allocations)
    {
        var allocatedMinor = allocations.GetValueOrDefault(obligation.Id);
        if (obligation.AmountMinor <= 0
            || allocatedMinor < 0
            || allocatedMinor > obligation.AmountMinor
            || !balances.ContainsKey(obligation.PartyId))
        {
            return false;
        }

        balances[obligation.PartyId] = checked(
            balances[obligation.PartyId] + obligation.AmountMinor - allocatedMinor);
        return true;
    }

    private sealed record Obligation(long Id, long PartyId, long AmountMinor);
}
