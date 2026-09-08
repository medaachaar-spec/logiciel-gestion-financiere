using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.SupplierAdvances;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.SupplierAdvances;

internal sealed class SupplierAdvanceService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    TimeProvider timeProvider) : ISupplierAdvanceService
{
    public async Task<ApplySupplierAdvanceResult> ApplyAsync(
        ApplySupplierAdvanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is null)
        {
            return Failed(ApplySupplierAdvanceFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.SupplierPayments,
                PermissionAction.Modify,
                cancellationToken))
        {
            return Failed(ApplySupplierAdvanceFailureReason.Forbidden);
        }

        if (command.SupplierPaymentId <= 0)
        {
            return Failed(ApplySupplierAdvanceFailureReason.InvalidSourcePayment);
        }

        if (command.Allocations is null)
        {
            return Failed(ApplySupplierAdvanceFailureReason.MissingAllocations);
        }

        var requestedAllocations = command.Allocations.ToArray();
        if (requestedAllocations.Length == 0)
        {
            return Failed(ApplySupplierAdvanceFailureReason.MissingAllocations);
        }

        var purchaseIds = new HashSet<long>();
        var initialPayableIds = new HashSet<long>();

        foreach (var allocation in requestedAllocations)
        {
            if (allocation is null
                || allocation.AmountMinor <= 0
                || allocation.PurchaseId.HasValue == allocation.InitialSupplierPayableId.HasValue)
            {
                return Failed(ApplySupplierAdvanceFailureReason.InvalidAllocation);
            }

            if (allocation.PurchaseId is long purchaseId)
            {
                if (purchaseId <= 0)
                {
                    return Failed(ApplySupplierAdvanceFailureReason.InvalidAllocation);
                }

                if (!purchaseIds.Add(purchaseId))
                {
                    return Failed(ApplySupplierAdvanceFailureReason.DuplicateAllocationTarget);
                }
            }
            else if (allocation.InitialSupplierPayableId is long payableId)
            {
                if (payableId <= 0)
                {
                    return Failed(ApplySupplierAdvanceFailureReason.InvalidAllocation);
                }

                if (!initialPayableIds.Add(payableId))
                {
                    return Failed(ApplySupplierAdvanceFailureReason.DuplicateAllocationTarget);
                }
            }
        }

        // Microsoft.Data.Sqlite starts an IMMEDIATE transaction here: reserve the
        // writer before reading persisted availability, and keep it until commit.
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var sourcePayment = await dbContext.SupplierPayments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                payment => payment.Id == command.SupplierPaymentId,
                cancellationToken);

        if (sourcePayment is null)
        {
            return Failed(ApplySupplierAdvanceFailureReason.SourcePaymentNotFound);
        }

        if (sourcePayment.AmountMinor <= 0)
        {
            return Failed(ApplySupplierAdvanceFailureReason.DataIntegrityViolation);
        }

        var integrity = await AllocationIntegrity.ReadSupplierAsync(
            dbContext, purchaseIds, initialPayableIds, sourcePayment.Id, cancellationToken);
        if (integrity is null)
        {
            return Failed(ApplySupplierAdvanceFailureReason.DataIntegrityViolation);
        }
        var existingSourceAllocatedMinor = integrity.SourceAllocatedMinor;
        var availableAdvanceMinor = sourcePayment.AmountMinor - existingSourceAllocatedMinor;

        if (availableAdvanceMinor == 0)
        {
            return Failed(ApplySupplierAdvanceFailureReason.NoAvailableAdvance);
        }

        var appliedAmountMinor = 0L;
        foreach (var allocation in requestedAllocations)
        {
            if (allocation.AmountMinor > availableAdvanceMinor - appliedAmountMinor)
            {
                return Failed(ApplySupplierAdvanceFailureReason.AllocationsExceedAvailableAdvance);
            }

            appliedAmountMinor += allocation.AmountMinor;
        }

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
            return Failed(ApplySupplierAdvanceFailureReason.ObligationNotFound);
        }

        var existingPurchaseAllocations = integrity.Documents;
        var existingPayableAllocations = integrity.Initials;

        foreach (var allocation in requestedAllocations)
        {
            if (allocation.PurchaseId is long purchaseId)
            {
                var purchase = purchases[purchaseId];
                if (purchase.SupplierId != sourcePayment.SupplierId)
                {
                    return Failed(ApplySupplierAdvanceFailureReason.ObligationBelongsToDifferentSupplier);
                }

                existingPurchaseAllocations.TryGetValue(purchaseId, out var existingAllocatedMinor);
                if (purchase.TotalAmountMinor <= 0)
                {
                    return Failed(ApplySupplierAdvanceFailureReason.DataIntegrityViolation);
                }

                if (allocation.AmountMinor > purchase.TotalAmountMinor - existingAllocatedMinor)
                {
                    return Failed(ApplySupplierAdvanceFailureReason.AllocationExceedsOutstanding);
                }
            }
            else
            {
                var payableId = allocation.InitialSupplierPayableId!.Value;
                var payable = payables[payableId];
                if (payable.SupplierId != sourcePayment.SupplierId)
                {
                    return Failed(ApplySupplierAdvanceFailureReason.ObligationBelongsToDifferentSupplier);
                }

                existingPayableAllocations.TryGetValue(payableId, out var existingAllocatedMinor);
                if (payable.InitialAmountMinor <= 0)
                {
                    return Failed(ApplySupplierAdvanceFailureReason.DataIntegrityViolation);
                }

                if (allocation.AmountMinor > payable.InitialAmountMinor - existingAllocatedMinor)
                {
                    return Failed(ApplySupplierAdvanceFailureReason.AllocationExceedsOutstanding);
                }
            }
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        foreach (var allocation in requestedAllocations)
        {
            dbContext.SupplierPaymentAllocations.Add(new SupplierPaymentAllocation
            {
                SupplierPaymentId = sourcePayment.Id,
                PurchaseId = allocation.PurchaseId,
                InitialSupplierPayableId = allocation.InitialSupplierPayableId,
                AmountMinor = allocation.AmountMinor,
                CreatedAtUtc = createdAtUtc
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ApplySupplierAdvanceResult.Succeeded(
            sourcePayment.Id,
            sourcePayment.Reference,
            availableAdvanceMinor,
            appliedAmountMinor,
            availableAdvanceMinor - appliedAmountMinor,
            requestedAllocations.Length);
    }

    private static ApplySupplierAdvanceResult Failed(
        ApplySupplierAdvanceFailureReason failureReason) =>
        ApplySupplierAdvanceResult.Failed(failureReason);
}
