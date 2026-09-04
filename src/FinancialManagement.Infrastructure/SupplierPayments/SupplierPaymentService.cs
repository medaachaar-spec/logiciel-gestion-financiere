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

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

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

        var existingPurchaseAllocations = await dbContext.SupplierPaymentAllocations
            .AsNoTracking()
            .Where(allocation =>
                allocation.PurchaseId.HasValue && purchaseIds.Contains(allocation.PurchaseId.Value))
            .GroupBy(allocation => allocation.PurchaseId!.Value)
            .Select(group => new { Id = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.Id, item => item.AmountMinor, cancellationToken);

        var existingPayableAllocations = await dbContext.SupplierPaymentAllocations
            .AsNoTracking()
            .Where(allocation =>
                allocation.InitialSupplierPayableId.HasValue
                && initialPayableIds.Contains(allocation.InitialSupplierPayableId.Value))
            .GroupBy(allocation => allocation.InitialSupplierPayableId!.Value)
            .Select(group => new { Id = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.Id, item => item.AmountMinor, cancellationToken);

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
                EnsureExistingAllocationsAreValid(
                    existingAllocatedMinor,
                    purchase.TotalAmountMinor);
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
                EnsureExistingAllocationsAreValid(
                    existingAllocatedMinor,
                    payable.InitialAmountMinor);
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

    private static void EnsureExistingAllocationsAreValid(
        long allocatedAmountMinor,
        long obligationAmountMinor)
    {
        if (allocatedAmountMinor < 0 || allocatedAmountMinor > obligationAmountMinor)
        {
            throw new InvalidOperationException(
                "Existing supplier payment allocations exceed the obligation amount.");
        }
    }
}
