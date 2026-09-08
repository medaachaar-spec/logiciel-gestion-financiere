using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Sales;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.SupplierPayments;

internal sealed class SupplierPaymentService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    IBusinessReferenceGenerator referenceGenerator,
    TimeProvider timeProvider) : ISupplierPaymentService
{
    public async Task<SupplierPaymentListResult> GetPageAsync(
        SupplierPaymentListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return SupplierPaymentListResult.Failed(
                query,
                SupplierPaymentListFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.SupplierPayments,
                PermissionAction.View,
                cancellationToken))
        {
            return SupplierPaymentListResult.Failed(
                query,
                SupplierPaymentListFailureReason.Forbidden);
        }

        if (query.PageNumber < 1)
        {
            return SupplierPaymentListResult.Failed(
                query,
                SupplierPaymentListFailureReason.InvalidPageNumber);
        }

        if (query.PageSize is < 1 or > 200)
        {
            return SupplierPaymentListResult.Failed(
                query,
                SupplierPaymentListFailureReason.InvalidPageSize);
        }

        if (query.FromDate is DateOnly fromDate
            && query.ToDate is DateOnly toDate
            && fromDate > toDate)
        {
            return SupplierPaymentListResult.Failed(
                query,
                SupplierPaymentListFailureReason.InvalidDateRange);
        }

        if (query.PaymentMethod is PaymentMethod paymentMethod
            && !Enum.IsDefined(paymentMethod))
        {
            return SupplierPaymentListResult.Failed(
                query,
                SupplierPaymentListFailureReason.InvalidPaymentMethod);
        }

        var skip = ((long)query.PageNumber - 1) * query.PageSize;
        if (skip > int.MaxValue)
        {
            return SupplierPaymentListResult.Failed(
                query,
                SupplierPaymentListFailureReason.InvalidPageNumber);
        }

        var payments = BuildHeaderReadQuery();
        if (query.FromDate is DateOnly inclusiveFromDate)
        {
            payments = payments.Where(payment => payment.PaymentDate >= inclusiveFromDate);
        }

        if (query.ToDate is DateOnly inclusiveToDate)
        {
            payments = payments.Where(payment => payment.PaymentDate <= inclusiveToDate);
        }

        var searchText = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(searchText))
        {
            payments = payments.Where(payment =>
                payment.Reference.Contains(searchText)
                || (payment.SupplierName != null && payment.SupplierName.Contains(searchText)));
        }

        if (query.PaymentMethod is PaymentMethod exactPaymentMethod)
        {
            payments = payments.Where(payment => payment.PaymentMethod == exactPaymentMethod);
        }

        var totalCount = await payments.CountAsync(cancellationToken);
        var page = await payments
            .OrderByDescending(payment => payment.PaymentDate)
            .ThenByDescending(payment => payment.Id)
            .Skip((int)skip)
            .Take(query.PageSize)
            .Select(payment => new SupplierPaymentListProjection
            {
                Id = payment.Id,
                Reference = payment.Reference,
                PaymentDate = payment.PaymentDate,
                SupplierId = payment.SupplierId,
                SupplierName = payment.SupplierName,
                AmountMinor = payment.AmountMinor,
                PaymentMethod = payment.PaymentMethod,
                Observation = payment.Observation
            })
            .ToListAsync(cancellationToken);

        var pageById = page.ToDictionary(payment => payment.Id);
        var pageIds = pageById.Keys.ToArray();
        var storedAmounts = await dbContext.SupplierPaymentAllocations.AsNoTracking()
            .Where(allocation => pageIds.Contains(allocation.SupplierPaymentId))
            .Select(allocation => new { PaymentId = allocation.SupplierPaymentId, allocation.AmountMinor })
            .ToListAsync(cancellationToken);
        var totals = new Dictionary<long, long>();
        foreach (var allocation in storedAmounts)
        {
            if (!AllocationIntegrity.TryAccumulate(totals, allocation.PaymentId,
                    allocation.AmountMinor, pageById[allocation.PaymentId].AmountMinor))
            {
                return SupplierPaymentListResult.Failed(
                    query, SupplierPaymentListFailureReason.DataIntegrityViolation);
            }
        }
        foreach (var payment in page)
            payment.AllocatedAmountMinor = totals.GetValueOrDefault(payment.Id);

        if (page.Any(payment =>
                payment.SupplierName is null
                || !HasValidAggregate(payment.AmountMinor, payment.AllocatedAmountMinor)))
        {
            return SupplierPaymentListResult.Failed(
                query,
                SupplierPaymentListFailureReason.DataIntegrityViolation);
        }

        var items = page
            .Select(payment => new SupplierPaymentListItem(
                payment.Id,
                payment.Reference,
                payment.PaymentDate,
                payment.SupplierId,
                payment.SupplierName!,
                payment.AmountMinor,
                payment.PaymentMethod,
                payment.AllocatedAmountMinor,
                payment.AmountMinor - payment.AllocatedAmountMinor,
                payment.Observation))
            .ToList();

        return SupplierPaymentListResult.Succeeded(query, items, totalCount);
    }

    public async Task<SupplierPaymentDetailResult> GetByIdAsync(
        long supplierPaymentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return SupplierPaymentDetailResult.Failed(
                SupplierPaymentDetailFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.SupplierPayments,
                PermissionAction.View,
                cancellationToken))
        {
            return SupplierPaymentDetailResult.Failed(
                SupplierPaymentDetailFailureReason.Forbidden);
        }

        if (supplierPaymentId <= 0)
        {
            return SupplierPaymentDetailResult.Failed(
                SupplierPaymentDetailFailureReason.InvalidPaymentId);
        }

        var payment = await BuildHeaderReadQuery()
            .SingleOrDefaultAsync(item => item.Id == supplierPaymentId, cancellationToken);

        if (payment is null)
        {
            return SupplierPaymentDetailResult.Failed(
                SupplierPaymentDetailFailureReason.NotFound);
        }

        if (payment.SupplierName is null)
        {
            return SupplierPaymentDetailResult.Failed(
                SupplierPaymentDetailFailureReason.DataIntegrityViolation);
        }

        var storedAllocations = await (
            from allocation in dbContext.SupplierPaymentAllocations.AsNoTracking()
            where allocation.SupplierPaymentId == supplierPaymentId
            join purchase in dbContext.Purchases.AsNoTracking()
                on allocation.PurchaseId equals (long?)purchase.Id into purchases
            from purchase in purchases.DefaultIfEmpty()
            join payable in dbContext.InitialSupplierPayables.AsNoTracking()
                on allocation.InitialSupplierPayableId equals (long?)payable.Id into payables
            from payable in payables.DefaultIfEmpty()
            orderby allocation.Id
            select new SupplierPaymentAllocationProjection
            {
                Id = allocation.Id,
                PurchaseId = allocation.PurchaseId,
                PurchaseReference = purchase == null ? null : purchase.Reference,
                PurchaseAmountMinor = purchase == null ? null : purchase.TotalAmountMinor,
                InitialSupplierPayableId = allocation.InitialSupplierPayableId,
                InitialSupplierPayableReference = payable == null ? null : payable.Reference,
                InitialSupplierPayableAmountMinor = payable == null
                    ? null
                    : payable.InitialAmountMinor,
                AllocatedAmountMinor = allocation.AmountMinor
            })
            .ToListAsync(cancellationToken);

        var allocations = new List<SupplierPaymentAllocationDetailItem>(storedAllocations.Count);
        var allocatedAmountMinor = 0L;
        foreach (var allocation in storedAllocations)
        {
            SupplierPaymentAllocationDetailItem item;
            if (allocation.PurchaseId is long purchaseId
                && allocation.InitialSupplierPayableId is null
                && allocation.PurchaseReference is not null
                && allocation.PurchaseAmountMinor is long purchaseAmountMinor)
            {
                item = new SupplierPaymentAllocationDetailItem(
                    allocation.Id,
                    SupplierPaymentAllocationTargetType.Purchase,
                    purchaseId,
                    allocation.PurchaseReference,
                    purchaseAmountMinor,
                    allocation.AllocatedAmountMinor);
            }
            else if (allocation.PurchaseId is null
                && allocation.InitialSupplierPayableId is long payableId
                && allocation.InitialSupplierPayableReference is not null
                && allocation.InitialSupplierPayableAmountMinor is long payableAmountMinor)
            {
                item = new SupplierPaymentAllocationDetailItem(
                    allocation.Id,
                    SupplierPaymentAllocationTargetType.InitialSupplierPayable,
                    payableId,
                    allocation.InitialSupplierPayableReference,
                    payableAmountMinor,
                    allocation.AllocatedAmountMinor);
            }
            else
            {
                return SupplierPaymentDetailResult.Failed(
                    SupplierPaymentDetailFailureReason.DataIntegrityViolation);
            }

            if (allocation.AllocatedAmountMinor <= 0
                || payment.AmountMinor <= 0
                || allocation.AllocatedAmountMinor > payment.AmountMinor - allocatedAmountMinor)
            {
                return SupplierPaymentDetailResult.Failed(
                    SupplierPaymentDetailFailureReason.DataIntegrityViolation);
            }

            allocatedAmountMinor = checked(allocatedAmountMinor + allocation.AllocatedAmountMinor);
            allocations.Add(item);
        }

        if (!HasValidAggregate(payment.AmountMinor, allocatedAmountMinor))
        {
            return SupplierPaymentDetailResult.Failed(
                SupplierPaymentDetailFailureReason.DataIntegrityViolation);
        }

        return SupplierPaymentDetailResult.Succeeded(new SupplierPaymentDetailItem(
            payment.Id,
            payment.Reference,
            payment.PaymentDate,
            payment.SupplierId,
            payment.SupplierName,
            payment.AmountMinor,
            payment.PaymentMethod,
            allocatedAmountMinor,
            payment.AmountMinor - allocatedAmountMinor,
            payment.Observation,
            allocations));
    }

    public async Task<CreateSupplierPaymentResult> CreateAsync(
        CreateSupplierPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is not long userId)
        {
            return Failed(CreateSupplierPaymentFailureReason.NotAuthenticated, command);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.SupplierPayments,
                PermissionAction.Create,
                cancellationToken))
        {
            return Failed(CreateSupplierPaymentFailureReason.Forbidden, command);
        }

        if (command.SupplierId <= 0)
        {
            return Failed(CreateSupplierPaymentFailureReason.InvalidSupplier, command);
        }

        if (command.AmountMinor <= 0)
        {
            return Failed(CreateSupplierPaymentFailureReason.InvalidAmount, command);
        }

        if (!Enum.IsDefined(command.PaymentMethod))
        {
            return Failed(CreateSupplierPaymentFailureReason.InvalidPaymentMethod, command);
        }

        if (command.Allocations is null)
        {
            return Failed(CreateSupplierPaymentFailureReason.MissingAllocations, command);
        }

        var requestedAllocations = command.Allocations.ToArray();
        if (requestedAllocations.Length == 0)
        {
            return Failed(CreateSupplierPaymentFailureReason.MissingAllocations, command);
        }

        var purchaseIds = new HashSet<long>();
        var initialPayableIds = new HashSet<long>();
        var allocatedAmountMinor = 0L;

        foreach (var allocation in requestedAllocations)
        {
            if (allocation is null
                || allocation.AmountMinor <= 0
                || allocation.PurchaseId.HasValue == allocation.InitialSupplierPayableId.HasValue)
            {
                return Failed(CreateSupplierPaymentFailureReason.InvalidAllocation, command);
            }

            if (allocation.AmountMinor > command.AmountMinor - allocatedAmountMinor)
            {
                return Failed(CreateSupplierPaymentFailureReason.AllocationsExceedPayment, command);
            }

            allocatedAmountMinor += allocation.AmountMinor;

            if (allocation.PurchaseId is long purchaseId)
            {
                if (purchaseId <= 0)
                {
                    return Failed(CreateSupplierPaymentFailureReason.InvalidAllocation, command);
                }

                if (!purchaseIds.Add(purchaseId))
                {
                    return Failed(CreateSupplierPaymentFailureReason.DuplicateAllocationTarget, command);
                }
            }
            else if (allocation.InitialSupplierPayableId is long payableId)
            {
                if (payableId <= 0)
                {
                    return Failed(CreateSupplierPaymentFailureReason.InvalidAllocation, command);
                }

                if (!initialPayableIds.Add(payableId))
                {
                    return Failed(CreateSupplierPaymentFailureReason.DuplicateAllocationTarget, command);
                }
            }
        }

        // Microsoft.Data.Sqlite starts an IMMEDIATE transaction here: reserve the
        // writer before reading persisted availability, and keep it until commit.
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var supplierExists = await dbContext.Suppliers
            .AsNoTracking()
            .AnyAsync(supplier => supplier.Id == command.SupplierId, cancellationToken);

        if (!supplierExists)
        {
            return Failed(CreateSupplierPaymentFailureReason.InvalidSupplier, command);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var localDateTime = TimeZoneInfo.ConvertTime(createdAtUtc, timeProvider.LocalTimeZone);
        var businessDate = DateOnly.FromDateTime(localDateTime.DateTime);

        var purchases = await dbContext.Purchases
            .AsNoTracking()
            .Where(purchase => purchaseIds.Contains(purchase.Id))
            .Select(purchase => new
            {
                purchase.Id,
                purchase.SupplierId,
                purchase.TotalAmountMinor
            })
            .ToDictionaryAsync(purchase => purchase.Id, cancellationToken);

        var payables = await dbContext.InitialSupplierPayables
            .AsNoTracking()
            .Where(payable => initialPayableIds.Contains(payable.Id))
            .Select(payable => new
            {
                payable.Id,
                payable.SupplierId,
                payable.InitialAmountMinor
            })
            .ToDictionaryAsync(payable => payable.Id, cancellationToken);

        if (purchases.Count != purchaseIds.Count || payables.Count != initialPayableIds.Count)
        {
            return Failed(CreateSupplierPaymentFailureReason.ObligationNotFound, command);
        }

        var integrity = await AllocationIntegrity.ReadSupplierAsync(
            dbContext, purchaseIds, initialPayableIds, null, cancellationToken);
        if (integrity is null)
        {
            return Failed(CreateSupplierPaymentFailureReason.DataIntegrityViolation, command);
        }

        var existingPurchaseAllocations = integrity.Documents;
        var existingPayableAllocations = integrity.Initials;

        foreach (var allocation in requestedAllocations)
        {
            if (allocation.PurchaseId is long purchaseId)
            {
                var purchase = purchases[purchaseId];
                if (purchase.SupplierId != command.SupplierId)
                {
                    return Failed(
                        CreateSupplierPaymentFailureReason.ObligationBelongsToDifferentSupplier,
                        command);
                }

                existingPurchaseAllocations.TryGetValue(
                    purchaseId,
                    out var existingAllocatedMinor);
                if (purchase.TotalAmountMinor <= 0)
                {
                    return Failed(CreateSupplierPaymentFailureReason.DataIntegrityViolation, command);
                }
                var outstandingMinor = purchase.TotalAmountMinor - existingAllocatedMinor;

                if (allocation.AmountMinor > outstandingMinor)
                {
                    return Failed(
                        CreateSupplierPaymentFailureReason.AllocationExceedsOutstanding,
                        command);
                }
            }
            else
            {
                var payableId = allocation.InitialSupplierPayableId!.Value;
                var payable = payables[payableId];
                if (payable.SupplierId != command.SupplierId)
                {
                    return Failed(
                        CreateSupplierPaymentFailureReason.ObligationBelongsToDifferentSupplier,
                        command);
                }

                existingPayableAllocations.TryGetValue(
                    payableId,
                    out var existingAllocatedMinor);
                if (payable.InitialAmountMinor <= 0)
                {
                    return Failed(CreateSupplierPaymentFailureReason.DataIntegrityViolation, command);
                }
                var outstandingMinor = payable.InitialAmountMinor - existingAllocatedMinor;

                if (allocation.AmountMinor > outstandingMinor)
                {
                    return Failed(
                        CreateSupplierPaymentFailureReason.AllocationExceedsOutstanding,
                        command);
                }
            }
        }

        var paymentReference = referenceGenerator.GenerateSupplierPaymentReference(businessDate);
        var payment = new SupplierPayment
        {
            Reference = paymentReference,
            SupplierId = command.SupplierId,
            PaymentDate = businessDate,
            AmountMinor = command.AmountMinor,
            PaymentMethod = command.PaymentMethod,
            Observation = command.Observation,
            CreatedByUserId = userId,
            CreatedAtUtc = createdAtUtc
        };
        dbContext.SupplierPayments.Add(payment);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var allocation in requestedAllocations)
        {
            dbContext.SupplierPaymentAllocations.Add(new SupplierPaymentAllocation
            {
                SupplierPaymentId = payment.Id,
                PurchaseId = allocation.PurchaseId,
                InitialSupplierPayableId = allocation.InitialSupplierPayableId,
                AmountMinor = allocation.AmountMinor,
                CreatedAtUtc = createdAtUtc
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateSupplierPaymentResult.Succeeded(
            payment.Id,
            payment.Reference,
            payment.AmountMinor,
            allocatedAmountMinor,
            payment.AmountMinor - allocatedAmountMinor,
            requestedAllocations.Length);
    }

    private static CreateSupplierPaymentResult Failed(
        CreateSupplierPaymentFailureReason failureReason,
        CreateSupplierPaymentCommand command) =>
        CreateSupplierPaymentResult.Failed(failureReason, command.AmountMinor);

    private IQueryable<SupplierPaymentHeaderProjection> BuildHeaderReadQuery() =>
        from payment in dbContext.SupplierPayments.AsNoTracking()
        join supplier in dbContext.Suppliers.AsNoTracking()
            on payment.SupplierId equals supplier.Id into suppliers
        from supplier in suppliers.DefaultIfEmpty()
        select new SupplierPaymentHeaderProjection
        {
            Id = payment.Id,
            Reference = payment.Reference,
            PaymentDate = payment.PaymentDate,
            SupplierId = payment.SupplierId,
            SupplierName = supplier == null ? null : supplier.Name,
            AmountMinor = payment.AmountMinor,
            PaymentMethod = payment.PaymentMethod,
            Observation = payment.Observation
        };

    private static bool HasValidAggregate(long amountMinor, long allocatedAmountMinor) =>
        amountMinor > 0 && allocatedAmountMinor >= 0 && allocatedAmountMinor <= amountMinor;

    private sealed class SupplierPaymentHeaderProjection
    {
        public long Id { get; init; }

        public required string Reference { get; init; }

        public DateOnly PaymentDate { get; init; }

        public long SupplierId { get; init; }

        public string? SupplierName { get; init; }

        public long AmountMinor { get; init; }

        public PaymentMethod PaymentMethod { get; init; }

        public string? Observation { get; init; }
    }

    private sealed class SupplierPaymentListProjection
    {
        public long Id { get; init; }

        public required string Reference { get; init; }

        public DateOnly PaymentDate { get; init; }

        public long SupplierId { get; init; }

        public string? SupplierName { get; init; }

        public long AmountMinor { get; init; }

        public PaymentMethod PaymentMethod { get; init; }

        public long AllocatedAmountMinor { get; set; }

        public string? Observation { get; init; }
    }

    private sealed class SupplierPaymentAllocationProjection
    {
        public long Id { get; init; }

        public long? PurchaseId { get; init; }

        public string? PurchaseReference { get; init; }

        public long? PurchaseAmountMinor { get; init; }

        public long? InitialSupplierPayableId { get; init; }

        public string? InitialSupplierPayableReference { get; init; }

        public long? InitialSupplierPayableAmountMinor { get; init; }

        public long AllocatedAmountMinor { get; init; }
    }
}
