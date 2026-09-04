using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.CustomerPayments;

internal sealed class CustomerPaymentService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    IBusinessReferenceGenerator referenceGenerator,
    TimeProvider timeProvider) : ICustomerPaymentService
{
    public async Task<CreateCustomerPaymentResult> CreateAsync(
        CreateCustomerPaymentCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is not long userId)
        {
            return Failed(CreateCustomerPaymentFailureReason.NotAuthenticated, command);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.CustomerPayments,
                PermissionAction.Create,
                cancellationToken))
        {
            return Failed(CreateCustomerPaymentFailureReason.Forbidden, command);
        }

        if (command.ClientId <= 0)
        {
            return Failed(CreateCustomerPaymentFailureReason.InvalidClient, command);
        }

        if (command.AmountMinor <= 0)
        {
            return Failed(CreateCustomerPaymentFailureReason.InvalidAmount, command);
        }

        if (!Enum.IsDefined(command.PaymentMethod))
        {
            return Failed(CreateCustomerPaymentFailureReason.InvalidPaymentMethod, command);
        }

        if (command.Allocations is null)
        {
            return Failed(CreateCustomerPaymentFailureReason.MissingAllocations, command);
        }

        var requestedAllocations = command.Allocations.ToArray();
        if (requestedAllocations.Length == 0)
        {
            return Failed(CreateCustomerPaymentFailureReason.MissingAllocations, command);
        }

        var saleIds = new HashSet<long>();
        var initialReceivableIds = new HashSet<long>();
        var allocatedAmountMinor = 0L;

        foreach (var allocation in requestedAllocations)
        {
            if (allocation is null
                || allocation.AmountMinor <= 0
                || allocation.SaleId.HasValue == allocation.InitialCustomerReceivableId.HasValue)
            {
                return Failed(CreateCustomerPaymentFailureReason.InvalidAllocation, command);
            }

            if (allocation.AmountMinor > command.AmountMinor - allocatedAmountMinor)
            {
                return Failed(CreateCustomerPaymentFailureReason.AllocationsExceedPayment, command);
            }

            allocatedAmountMinor += allocation.AmountMinor;

            if (allocation.SaleId is long saleId)
            {
                if (saleId <= 0)
                {
                    return Failed(CreateCustomerPaymentFailureReason.InvalidAllocation, command);
                }

                if (!saleIds.Add(saleId))
                {
                    return Failed(CreateCustomerPaymentFailureReason.DuplicateAllocationTarget, command);
                }
            }
            else if (allocation.InitialCustomerReceivableId is long receivableId)
            {
                if (receivableId <= 0)
                {
                    return Failed(CreateCustomerPaymentFailureReason.InvalidAllocation, command);
                }

                if (!initialReceivableIds.Add(receivableId))
                {
                    return Failed(CreateCustomerPaymentFailureReason.DuplicateAllocationTarget, command);
                }
            }
        }

        var clientExists = await dbContext.Clients
            .AsNoTracking()
            .AnyAsync(client => client.Id == command.ClientId, cancellationToken);

        if (!clientExists)
        {
            return Failed(CreateCustomerPaymentFailureReason.InvalidClient, command);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var localDateTime = TimeZoneInfo.ConvertTime(createdAtUtc, timeProvider.LocalTimeZone);
        var businessDate = DateOnly.FromDateTime(localDateTime.DateTime);

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

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
            return Failed(CreateCustomerPaymentFailureReason.ObligationNotFound, command);
        }

        var existingSaleAllocations = await dbContext.CustomerPaymentAllocations
            .AsNoTracking()
            .Where(allocation =>
                allocation.SaleId.HasValue && saleIds.Contains(allocation.SaleId.Value))
            .GroupBy(allocation => allocation.SaleId!.Value)
            .Select(group => new { Id = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.Id, item => item.AmountMinor, cancellationToken);

        var existingReceivableAllocations = await dbContext.CustomerPaymentAllocations
            .AsNoTracking()
            .Where(allocation =>
                allocation.InitialCustomerReceivableId.HasValue
                && initialReceivableIds.Contains(allocation.InitialCustomerReceivableId.Value))
            .GroupBy(allocation => allocation.InitialCustomerReceivableId!.Value)
            .Select(group => new { Id = group.Key, AmountMinor = group.Sum(item => item.AmountMinor) })
            .ToDictionaryAsync(item => item.Id, item => item.AmountMinor, cancellationToken);

        foreach (var allocation in requestedAllocations)
        {
            if (allocation.SaleId is long saleId)
            {
                var sale = sales[saleId];
                if (sale.ClientId != command.ClientId)
                {
                    return Failed(
                        CreateCustomerPaymentFailureReason.ObligationBelongsToDifferentClient,
                        command);
                }

                existingSaleAllocations.TryGetValue(saleId, out var existingAllocatedMinor);
                EnsureExistingAllocationsAreValid(existingAllocatedMinor, sale.TotalAmountMinor);
                var outstandingMinor = sale.TotalAmountMinor - existingAllocatedMinor;

                if (allocation.AmountMinor > outstandingMinor)
                {
                    return Failed(
                        CreateCustomerPaymentFailureReason.AllocationExceedsOutstanding,
                        command);
                }
            }
            else
            {
                var receivableId = allocation.InitialCustomerReceivableId!.Value;
                var receivable = receivables[receivableId];
                if (receivable.ClientId != command.ClientId)
                {
                    return Failed(
                        CreateCustomerPaymentFailureReason.ObligationBelongsToDifferentClient,
                        command);
                }

                existingReceivableAllocations.TryGetValue(
                    receivableId,
                    out var existingAllocatedMinor);
                EnsureExistingAllocationsAreValid(
                    existingAllocatedMinor,
                    receivable.InitialAmountMinor);
                var outstandingMinor = receivable.InitialAmountMinor - existingAllocatedMinor;

                if (allocation.AmountMinor > outstandingMinor)
                {
                    return Failed(
                        CreateCustomerPaymentFailureReason.AllocationExceedsOutstanding,
                        command);
                }
            }
        }

        var paymentReference = referenceGenerator.GenerateCustomerPaymentReference(businessDate);
        var payment = new CustomerPayment
        {
            Reference = paymentReference,
            ClientId = command.ClientId,
            PaymentDate = businessDate,
            AmountMinor = command.AmountMinor,
            PaymentMethod = command.PaymentMethod,
            Observation = command.Observation,
            CreatedByUserId = userId,
            CreatedAtUtc = createdAtUtc
        };
        dbContext.CustomerPayments.Add(payment);
        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var allocation in requestedAllocations)
        {
            dbContext.CustomerPaymentAllocations.Add(new CustomerPaymentAllocation
            {
                CustomerPaymentId = payment.Id,
                SaleId = allocation.SaleId,
                InitialCustomerReceivableId = allocation.InitialCustomerReceivableId,
                AmountMinor = allocation.AmountMinor,
                CreatedAtUtc = createdAtUtc
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateCustomerPaymentResult.Succeeded(
            payment.Id,
            payment.Reference,
            payment.AmountMinor,
            allocatedAmountMinor,
            payment.AmountMinor - allocatedAmountMinor,
            requestedAllocations.Length);
    }

    private static CreateCustomerPaymentResult Failed(
        CreateCustomerPaymentFailureReason failureReason,
        CreateCustomerPaymentCommand command) =>
        CreateCustomerPaymentResult.Failed(failureReason, command.AmountMinor);

    private static void EnsureExistingAllocationsAreValid(
        long allocatedAmountMinor,
        long obligationAmountMinor)
    {
        if (allocatedAmountMinor < 0 || allocatedAmountMinor > obligationAmountMinor)
        {
            throw new InvalidOperationException(
                "Existing customer payment allocations exceed the obligation amount.");
        }
    }
}
