using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure;

// Writers must read inside their transaction, before tracking any new allocation.
// The two dimensions are independent: a target can look payable while a payment
// contributing to it is already overallocated across several other targets.
internal static class AllocationIntegrity
{
    public static Task<Snapshot?> ReadCustomerAsync(
        FinancialManagementDbContext db, HashSet<long> documentIds, HashSet<long> initialIds,
        long? sourcePaymentId, CancellationToken cancellationToken) =>
        ReadAsync(
            from allocation in db.CustomerPaymentAllocations.AsNoTracking()
            join payment in db.CustomerPayments.AsNoTracking()
                on allocation.CustomerPaymentId equals payment.Id into payments
            from payment in payments.DefaultIfEmpty()
            join document in db.Sales.AsNoTracking()
                on allocation.SaleId equals (long?)document.Id into documents
            from document in documents.DefaultIfEmpty()
            join initial in db.InitialCustomerReceivables.AsNoTracking()
                on allocation.InitialCustomerReceivableId equals (long?)initial.Id into initials
            from initial in initials.DefaultIfEmpty()
            select new Row
            {
                PaymentId = allocation.CustomerPaymentId,
                PaymentAmountMinor = payment == null ? null : payment.AmountMinor,
                PaymentPartyId = payment == null ? null : payment.ClientId,
                PaymentReference = payment == null ? null : payment.Reference,
                PaymentMethod = payment == null ? null : payment.PaymentMethod,
                DocumentId = allocation.SaleId,
                InitialId = allocation.InitialCustomerReceivableId,
                TargetAmountMinor = allocation.SaleId.HasValue
                    ? (document == null ? null : document.TotalAmountMinor)
                    : (initial == null ? null : initial.InitialAmountMinor),
                TargetPartyId = allocation.SaleId.HasValue
                    ? (document == null ? null : document.ClientId)
                    : (initial == null ? null : initial.ClientId),
                AmountMinor = allocation.AmountMinor
            },
            documentIds, initialIds, sourcePaymentId, cancellationToken);

    public static Task<Snapshot?> ReadSupplierAsync(
        FinancialManagementDbContext db, HashSet<long> documentIds, HashSet<long> initialIds,
        long? sourcePaymentId, CancellationToken cancellationToken) =>
        ReadAsync(
            from allocation in db.SupplierPaymentAllocations.AsNoTracking()
            join payment in db.SupplierPayments.AsNoTracking()
                on allocation.SupplierPaymentId equals payment.Id into payments
            from payment in payments.DefaultIfEmpty()
            join document in db.Purchases.AsNoTracking()
                on allocation.PurchaseId equals (long?)document.Id into documents
            from document in documents.DefaultIfEmpty()
            join initial in db.InitialSupplierPayables.AsNoTracking()
                on allocation.InitialSupplierPayableId equals (long?)initial.Id into initials
            from initial in initials.DefaultIfEmpty()
            select new Row
            {
                PaymentId = allocation.SupplierPaymentId,
                PaymentAmountMinor = payment == null ? null : payment.AmountMinor,
                PaymentPartyId = payment == null ? null : payment.SupplierId,
                PaymentReference = payment == null ? null : payment.Reference,
                PaymentMethod = payment == null ? null : payment.PaymentMethod,
                DocumentId = allocation.PurchaseId,
                InitialId = allocation.InitialSupplierPayableId,
                TargetAmountMinor = allocation.PurchaseId.HasValue
                    ? (document == null ? null : document.TotalAmountMinor)
                    : (initial == null ? null : initial.InitialAmountMinor),
                TargetPartyId = allocation.PurchaseId.HasValue
                    ? (document == null ? null : document.SupplierId)
                    : (initial == null ? null : initial.SupplierId),
                AmountMinor = allocation.AmountMinor
            },
            documentIds, initialIds, sourcePaymentId, cancellationToken);

    private static async Task<Snapshot?> ReadAsync(
        IQueryable<Row> query, HashSet<long> requestedDocumentIds, HashSet<long> requestedInitialIds,
        long? sourcePaymentId, CancellationToken cancellationToken)
    {
        var documentIds = new HashSet<long>(requestedDocumentIds);
        var initialIds = new HashSet<long>(requestedInitialIds);
        if (sourcePaymentId.HasValue)
        {
            // Also validate obligations already funded by this advance's source.
            var sourceRows = await query.Where(row => row.PaymentId == sourcePaymentId.Value)
                .ToListAsync(cancellationToken);
            foreach (var row in sourceRows)
            {
                if (!IsValidRow(row))
                    return null;
                if (row.DocumentId is long documentId)
                    documentIds.Add(documentId);
                else
                    initialIds.Add(row.InitialId!.Value);
            }
        }

        var targetRows = await query.Where(row =>
                (row.DocumentId.HasValue && documentIds.Contains(row.DocumentId.Value))
                || (row.InitialId.HasValue && initialIds.Contains(row.InitialId.Value)))
            .ToListAsync(cancellationToken);
        var snapshot = new Snapshot();
        foreach (var row in targetRows)
        {
            if (!IsValidRow(row))
                return null;
            var totals = row.DocumentId.HasValue ? snapshot.Documents : snapshot.Initials;
            var id = row.DocumentId ?? row.InitialId!.Value;
            if (!TryAccumulate(totals, id, row.AmountMinor, row.TargetAmountMinor!.Value))
                return null;
        }

        var paymentIds = targetRows.Select(row => row.PaymentId).ToHashSet();
        if (sourcePaymentId.HasValue)
            paymentIds.Add(sourcePaymentId.Value);
        // Include every allocation of contributing payments, not just allocations
        // to the requested obligations. SQL SUM could overflow before validation.
        var paymentRows = await query.Where(row => paymentIds.Contains(row.PaymentId))
            .ToListAsync(cancellationToken);
        var paymentTotals = new Dictionary<long, long>();
        foreach (var row in paymentRows)
        {
            if (!IsValidRow(row)
                || !TryAccumulate(paymentTotals, row.PaymentId, row.AmountMinor,
                    row.PaymentAmountMinor!.Value))
                return null;
        }

        snapshot.SourceAllocatedMinor = sourcePaymentId.HasValue
            ? paymentTotals.GetValueOrDefault(sourcePaymentId.Value)
            : 0;
        return snapshot;
    }

    private static bool IsValidRow(Row row) =>
        row.AmountMinor > 0
        && row.PaymentAmountMinor is > 0
        && row.PaymentPartyId == row.TargetPartyId
        && !string.IsNullOrWhiteSpace(row.PaymentReference)
        && row.PaymentMethod is PaymentMethod paymentMethod
        && Enum.IsDefined(paymentMethod)
        && row.DocumentId.HasValue != row.InitialId.HasValue
        && row.TargetAmountMinor is > 0
        && row.AmountMinor <= row.TargetAmountMinor.Value;

    internal static bool TryAccumulate(Dictionary<long, long> totals, long id, long amount, long maximum)
    {
        if (amount <= 0 || maximum <= 0)
            return false;
        var current = totals.GetValueOrDefault(id);
        // Positive inputs and this subtraction bound make addition safe even at
        // long.MaxValue. An impossible aggregate fails; no clipping or repair.
        if (amount > maximum - current)
            return false;
        totals[id] = checked(current + amount);
        return true;
    }

    internal sealed class Snapshot
    {
        public Dictionary<long, long> Documents { get; } = [];
        public Dictionary<long, long> Initials { get; } = [];
        public long SourceAllocatedMinor { get; set; }
    }

    private sealed class Row
    {
        public long PaymentId { get; init; }
        public long? PaymentAmountMinor { get; init; }
        public long? PaymentPartyId { get; init; }
        public string? PaymentReference { get; init; }
        public PaymentMethod? PaymentMethod { get; init; }
        public long? DocumentId { get; init; }
        public long? InitialId { get; init; }
        public long? TargetAmountMinor { get; init; }
        public long? TargetPartyId { get; init; }
        public long AmountMinor { get; init; }
    }
}
