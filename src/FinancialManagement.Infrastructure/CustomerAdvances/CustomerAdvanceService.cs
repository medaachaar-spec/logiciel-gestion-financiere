using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.CustomerAdvances;

internal sealed class CustomerAdvanceService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    TimeProvider timeProvider) : ICustomerAdvanceService
{
    public async Task<ApplyCustomerAdvanceResult> ApplyAsync(
        ApplyCustomerAdvanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is null)
        {
            return Failed(ApplyCustomerAdvanceFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.CustomerPayments,
                PermissionAction.Modify,
                cancellationToken))
        {
            return Failed(ApplyCustomerAdvanceFailureReason.Forbidden);
        }

        if (command.CustomerPaymentId <= 0)
        {
            return Failed(ApplyCustomerAdvanceFailureReason.InvalidSourcePayment);
        }

        if (command.Allocations is null)
        {
            return Failed(ApplyCustomerAdvanceFailureReason.MissingAllocations);
        }

        var requestedAllocations = command.Allocations.ToArray();
        if (requestedAllocations.Length == 0)
        {
            return Failed(ApplyCustomerAdvanceFailureReason.MissingAllocations);
        }

        var saleIds = new HashSet<long>();
        var initialReceivableIds = new HashSet<long>();

        foreach (var allocation in requestedAllocations)
        {
            if (allocation is null
                || allocation.AmountMinor <= 0
                || allocation.SaleId.HasValue == allocation.InitialCustomerReceivableId.HasValue)
            {
                return Failed(ApplyCustomerAdvanceFailureReason.InvalidAllocation);
            }

            if (allocation.SaleId is long saleId)
            {
                if (saleId <= 0)
                {
                    return Failed(ApplyCustomerAdvanceFailureReason.InvalidAllocation);
                }

                if (!saleIds.Add(saleId))
                {
                    return Failed(ApplyCustomerAdvanceFailureReason.DuplicateAllocationTarget);
                }
            }
            else if (allocation.InitialCustomerReceivableId is long receivableId)
            {
                if (receivableId <= 0)
                {
                    return Failed(ApplyCustomerAdvanceFailureReason.InvalidAllocation);
                }

                if (!initialReceivableIds.Add(receivableId))
                {
                    return Failed(ApplyCustomerAdvanceFailureReason.DuplicateAllocationTarget);
                }
            }
        }

        // Microsoft.Data.Sqlite starts an IMMEDIATE transaction here: reserve the
        // writer before reading persisted availability, and keep it until commit.
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var sourcePayment = await dbContext.CustomerPayments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                payment => payment.Id == command.CustomerPaymentId,
                cancellationToken);

        if (sourcePayment is null)
        {
            return Failed(ApplyCustomerAdvanceFailureReason.SourcePaymentNotFound);
        }

        if (sourcePayment.ClientId is not long clientId)
        {
            return Failed(ApplyCustomerAdvanceFailureReason.SourcePaymentNotEligible);
        }

        if (sourcePayment.AmountMinor <= 0)
        {
            return Failed(ApplyCustomerAdvanceFailureReason.DataIntegrityViolation);
        }

        var integrity = await AllocationIntegrity.ReadCustomerAsync(
            dbContext, saleIds, initialReceivableIds, sourcePayment.Id, cancellationToken);
        if (integrity is null)
        {
            return Failed(ApplyCustomerAdvanceFailureReason.DataIntegrityViolation);
        }
        var existingSourceAllocatedMinor = integrity.SourceAllocatedMinor;
        var availableAdvanceMinor = sourcePayment.AmountMinor - existingSourceAllocatedMinor;

        if (availableAdvanceMinor == 0)
        {
            return Failed(ApplyCustomerAdvanceFailureReason.NoAvailableAdvance);
        }

        var appliedAmountMinor = 0L;
        foreach (var allocation in requestedAllocations)
        {
            if (allocation.AmountMinor > availableAdvanceMinor - appliedAmountMinor)
            {
                return Failed(ApplyCustomerAdvanceFailureReason.AllocationsExceedAvailableAdvance);
            }

            appliedAmountMinor += allocation.AmountMinor;
        }

        var sales = await dbContext.Sales
            .AsNoTracking()
            .Where(sale => saleIds.Contains(sale.Id))
            .Select(sale => new { sale.Id, sale.ClientId, sale.TotalAmountMinor })
            .ToDictionaryAsync(sale => sale.Id, cancellationToken);

        var receivables = await dbContext.InitialCustomerReceivables
            .AsNoTracking()
            .Where(receivable => initialReceivableIds.Contains(receivable.Id))
            .Select(receivable => new
            {
                receivable.Id,
                receivable.ClientId,
                receivable.InitialAmountMinor
            })
            .ToDictionaryAsync(receivable => receivable.Id, cancellationToken);

        if (sales.Count != saleIds.Count || receivables.Count != initialReceivableIds.Count)
        {
            return Failed(ApplyCustomerAdvanceFailureReason.ObligationNotFound);
        }

        var existingSaleAllocations = integrity.Documents;
        var existingReceivableAllocations = integrity.Initials;

        foreach (var allocation in requestedAllocations)
        {
            if (allocation.SaleId is long saleId)
            {
                var sale = sales[saleId];
                if (sale.ClientId != clientId)
                {
                    return Failed(ApplyCustomerAdvanceFailureReason.ObligationBelongsToDifferentClient);
                }

                existingSaleAllocations.TryGetValue(saleId, out var existingAllocatedMinor);
                if (sale.TotalAmountMinor <= 0)
                {
                    return Failed(ApplyCustomerAdvanceFailureReason.DataIntegrityViolation);
                }

                if (allocation.AmountMinor > sale.TotalAmountMinor - existingAllocatedMinor)
                {
                    return Failed(ApplyCustomerAdvanceFailureReason.AllocationExceedsOutstanding);
                }
            }
            else
            {
                var receivableId = allocation.InitialCustomerReceivableId!.Value;
                var receivable = receivables[receivableId];
                if (receivable.ClientId != clientId)
                {
                    return Failed(ApplyCustomerAdvanceFailureReason.ObligationBelongsToDifferentClient);
                }

                existingReceivableAllocations.TryGetValue(
                    receivableId,
                    out var existingAllocatedMinor);
                if (receivable.InitialAmountMinor <= 0)
                {
                    return Failed(ApplyCustomerAdvanceFailureReason.DataIntegrityViolation);
                }

                if (allocation.AmountMinor > receivable.InitialAmountMinor - existingAllocatedMinor)
                {
                    return Failed(ApplyCustomerAdvanceFailureReason.AllocationExceedsOutstanding);
                }
            }
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        foreach (var allocation in requestedAllocations)
        {
            dbContext.CustomerPaymentAllocations.Add(new CustomerPaymentAllocation
            {
                CustomerPaymentId = sourcePayment.Id,
                SaleId = allocation.SaleId,
                InitialCustomerReceivableId = allocation.InitialCustomerReceivableId,
                AmountMinor = allocation.AmountMinor,
                CreatedAtUtc = createdAtUtc
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return ApplyCustomerAdvanceResult.Succeeded(
            sourcePayment.Id,
            sourcePayment.Reference,
            availableAdvanceMinor,
            appliedAmountMinor,
            availableAdvanceMinor - appliedAmountMinor,
            requestedAllocations.Length);
    }

    private static ApplyCustomerAdvanceResult Failed(
        ApplyCustomerAdvanceFailureReason failureReason) =>
        ApplyCustomerAdvanceResult.Failed(failureReason);
}
