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
    public async Task<CustomerPaymentListResult> GetPageAsync(
        CustomerPaymentListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return CustomerPaymentListResult.Failed(
                query,
                CustomerPaymentListFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.CustomerPayments,
                PermissionAction.View,
                cancellationToken))
        {
            return CustomerPaymentListResult.Failed(
                query,
                CustomerPaymentListFailureReason.Forbidden);
        }

        if (query.PageNumber < 1)
        {
            return CustomerPaymentListResult.Failed(
                query,
                CustomerPaymentListFailureReason.InvalidPageNumber);
        }

        if (query.PageSize is < 1 or > 200)
        {
            return CustomerPaymentListResult.Failed(
                query,
                CustomerPaymentListFailureReason.InvalidPageSize);
        }

        if (query.FromDate is DateOnly fromDate
            && query.ToDate is DateOnly toDate
            && fromDate > toDate)
        {
            return CustomerPaymentListResult.Failed(
                query,
                CustomerPaymentListFailureReason.InvalidDateRange);
        }

        if (query.PaymentMethod is PaymentMethod paymentMethod
            && !Enum.IsDefined(paymentMethod))
        {
            return CustomerPaymentListResult.Failed(
                query,
                CustomerPaymentListFailureReason.InvalidPaymentMethod);
        }

        var skip = ((long)query.PageNumber - 1) * query.PageSize;
        if (skip > int.MaxValue)
        {
            return CustomerPaymentListResult.Failed(
                query,
                CustomerPaymentListFailureReason.InvalidPageNumber);
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
                || (payment.ClientName != null && payment.ClientName.Contains(searchText)));
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
            .Select(payment => new CustomerPaymentListProjection
            {
                Id = payment.Id,
                Reference = payment.Reference,
                PaymentDate = payment.PaymentDate,
                ClientId = payment.ClientId,
                ClientName = payment.ClientName,
                AmountMinor = payment.AmountMinor,
                PaymentMethod = payment.PaymentMethod,
                Observation = payment.Observation
            })
            .ToListAsync(cancellationToken);

        var pageById = page.ToDictionary(payment => payment.Id);
        var pageIds = pageById.Keys.ToArray();
        var storedAmounts = await dbContext.CustomerPaymentAllocations.AsNoTracking()
            .Where(allocation => pageIds.Contains(allocation.CustomerPaymentId))
            .Select(allocation => new { PaymentId = allocation.CustomerPaymentId, allocation.AmountMinor })
            .ToListAsync(cancellationToken);
        var totals = new Dictionary<long, long>();
        foreach (var allocation in storedAmounts)
        {
            if (!AllocationIntegrity.TryAccumulate(totals, allocation.PaymentId,
                    allocation.AmountMinor, pageById[allocation.PaymentId].AmountMinor))
            {
                return CustomerPaymentListResult.Failed(
                    query, CustomerPaymentListFailureReason.DataIntegrityViolation);
            }
        }
        foreach (var payment in page)
            payment.AllocatedAmountMinor = totals.GetValueOrDefault(payment.Id);

        if (page.Any(payment =>
                !HasValidParty(payment.ClientId, payment.ClientName)
                || !HasValidAggregate(payment.AmountMinor, payment.AllocatedAmountMinor)))
        {
            return CustomerPaymentListResult.Failed(
                query,
                CustomerPaymentListFailureReason.DataIntegrityViolation);
        }

        var items = page
            .Select(payment => new CustomerPaymentListItem(
                payment.Id,
                payment.Reference,
                payment.PaymentDate,
                payment.ClientId,
                payment.ClientName,
                payment.AmountMinor,
                payment.PaymentMethod,
                payment.AllocatedAmountMinor,
                payment.AmountMinor - payment.AllocatedAmountMinor,
                payment.Observation))
            .ToList();

        return CustomerPaymentListResult.Succeeded(query, items, totalCount);
    }

    public async Task<CustomerPaymentDetailResult> GetByIdAsync(
        long customerPaymentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return CustomerPaymentDetailResult.Failed(
                CustomerPaymentDetailFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.CustomerPayments,
                PermissionAction.View,
                cancellationToken))
        {
            return CustomerPaymentDetailResult.Failed(
                CustomerPaymentDetailFailureReason.Forbidden);
        }

        if (customerPaymentId <= 0)
        {
            return CustomerPaymentDetailResult.Failed(
                CustomerPaymentDetailFailureReason.InvalidPaymentId);
        }

        var payment = await BuildHeaderReadQuery()
            .SingleOrDefaultAsync(item => item.Id == customerPaymentId, cancellationToken);

        if (payment is null)
        {
            return CustomerPaymentDetailResult.Failed(
                CustomerPaymentDetailFailureReason.NotFound);
        }

        if (!HasValidParty(payment.ClientId, payment.ClientName))
        {
            return CustomerPaymentDetailResult.Failed(
                CustomerPaymentDetailFailureReason.DataIntegrityViolation);
        }

        var storedAllocations = await (
            from allocation in dbContext.CustomerPaymentAllocations.AsNoTracking()
            where allocation.CustomerPaymentId == customerPaymentId
            join sale in dbContext.Sales.AsNoTracking()
                on allocation.SaleId equals (long?)sale.Id into sales
            from sale in sales.DefaultIfEmpty()
            join receivable in dbContext.InitialCustomerReceivables.AsNoTracking()
                on allocation.InitialCustomerReceivableId equals (long?)receivable.Id into receivables
            from receivable in receivables.DefaultIfEmpty()
            orderby allocation.Id
            select new CustomerPaymentAllocationProjection
            {
                Id = allocation.Id,
                SaleId = allocation.SaleId,
                SaleReference = sale == null ? null : sale.Reference,
                SaleAmountMinor = sale == null ? null : sale.TotalAmountMinor,
                InitialCustomerReceivableId = allocation.InitialCustomerReceivableId,
                InitialCustomerReceivableReference = receivable == null ? null : receivable.Reference,
                InitialCustomerReceivableAmountMinor = receivable == null
                    ? null
                    : receivable.InitialAmountMinor,
                AllocatedAmountMinor = allocation.AmountMinor
            })
            .ToListAsync(cancellationToken);

        var allocations = new List<CustomerPaymentAllocationDetailItem>(storedAllocations.Count);
        var allocatedAmountMinor = 0L;
        foreach (var allocation in storedAllocations)
        {
            CustomerPaymentAllocationDetailItem item;
            if (allocation.SaleId is long saleId
                && allocation.InitialCustomerReceivableId is null
                && allocation.SaleReference is not null
                && allocation.SaleAmountMinor is long saleAmountMinor)
            {
                item = new CustomerPaymentAllocationDetailItem(
                    allocation.Id,
                    CustomerPaymentAllocationTargetType.Sale,
                    saleId,
                    allocation.SaleReference,
                    saleAmountMinor,
                    allocation.AllocatedAmountMinor);
            }
            else if (allocation.SaleId is null
                && allocation.InitialCustomerReceivableId is long receivableId
                && allocation.InitialCustomerReceivableReference is not null
                && allocation.InitialCustomerReceivableAmountMinor is long receivableAmountMinor)
            {
                item = new CustomerPaymentAllocationDetailItem(
                    allocation.Id,
                    CustomerPaymentAllocationTargetType.InitialCustomerReceivable,
                    receivableId,
                    allocation.InitialCustomerReceivableReference,
                    receivableAmountMinor,
                    allocation.AllocatedAmountMinor);
            }
            else
            {
                return CustomerPaymentDetailResult.Failed(
                    CustomerPaymentDetailFailureReason.DataIntegrityViolation);
            }

            if (allocation.AllocatedAmountMinor <= 0
                || payment.AmountMinor <= 0
                || allocation.AllocatedAmountMinor > payment.AmountMinor - allocatedAmountMinor)
            {
                return CustomerPaymentDetailResult.Failed(
                    CustomerPaymentDetailFailureReason.DataIntegrityViolation);
            }

            allocatedAmountMinor = checked(allocatedAmountMinor + allocation.AllocatedAmountMinor);
            allocations.Add(item);
        }

        if (!HasValidAggregate(payment.AmountMinor, allocatedAmountMinor))
        {
            return CustomerPaymentDetailResult.Failed(
                CustomerPaymentDetailFailureReason.DataIntegrityViolation);
        }

        return CustomerPaymentDetailResult.Succeeded(new CustomerPaymentDetailItem(
            payment.Id,
            payment.Reference,
            payment.PaymentDate,
            payment.ClientId,
            payment.ClientName,
            payment.AmountMinor,
            payment.PaymentMethod,
            allocatedAmountMinor,
            payment.AmountMinor - allocatedAmountMinor,
            payment.Observation,
            allocations));
    }

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

        // Microsoft.Data.Sqlite starts an IMMEDIATE transaction here: reserve the
        // writer before reading persisted availability, and keep it until commit.
        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

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

        var integrity = await AllocationIntegrity.ReadCustomerAsync(
            dbContext, saleIds, initialReceivableIds, null, cancellationToken);
        if (integrity is null)
        {
            return Failed(CreateCustomerPaymentFailureReason.DataIntegrityViolation, command);
        }

        var existingSaleAllocations = integrity.Documents;
        var existingReceivableAllocations = integrity.Initials;

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
                if (sale.TotalAmountMinor <= 0)
                {
                    return Failed(CreateCustomerPaymentFailureReason.DataIntegrityViolation, command);
                }
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
                if (receivable.InitialAmountMinor <= 0)
                {
                    return Failed(CreateCustomerPaymentFailureReason.DataIntegrityViolation, command);
                }
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

    private IQueryable<CustomerPaymentHeaderProjection> BuildHeaderReadQuery() =>
        from payment in dbContext.CustomerPayments.AsNoTracking()
        join client in dbContext.Clients.AsNoTracking()
            on payment.ClientId equals (long?)client.Id into clients
        from client in clients.DefaultIfEmpty()
        select new CustomerPaymentHeaderProjection
        {
            Id = payment.Id,
            Reference = payment.Reference,
            PaymentDate = payment.PaymentDate,
            ClientId = payment.ClientId,
            ClientName = client == null ? null : client.Name,
            AmountMinor = payment.AmountMinor,
            PaymentMethod = payment.PaymentMethod,
            Observation = payment.Observation
        };

    private static bool HasValidParty(long? clientId, string? clientName) =>
        clientId is null || clientName is not null;

    private static bool HasValidAggregate(long amountMinor, long allocatedAmountMinor) =>
        amountMinor > 0 && allocatedAmountMinor >= 0 && allocatedAmountMinor <= amountMinor;

    private sealed class CustomerPaymentHeaderProjection
    {
        public long Id { get; init; }

        public required string Reference { get; init; }

        public DateOnly PaymentDate { get; init; }

        public long? ClientId { get; init; }

        public string? ClientName { get; init; }

        public long AmountMinor { get; init; }

        public PaymentMethod PaymentMethod { get; init; }

        public string? Observation { get; init; }
    }

    private sealed class CustomerPaymentListProjection
    {
        public long Id { get; init; }

        public required string Reference { get; init; }

        public DateOnly PaymentDate { get; init; }

        public long? ClientId { get; init; }

        public string? ClientName { get; init; }

        public long AmountMinor { get; init; }

        public PaymentMethod PaymentMethod { get; init; }

        public long AllocatedAmountMinor { get; set; }

        public string? Observation { get; init; }
    }

    private sealed class CustomerPaymentAllocationProjection
    {
        public long Id { get; init; }

        public long? SaleId { get; init; }

        public string? SaleReference { get; init; }

        public long? SaleAmountMinor { get; init; }

        public long? InitialCustomerReceivableId { get; init; }

        public string? InitialCustomerReceivableReference { get; init; }

        public long? InitialCustomerReceivableAmountMinor { get; init; }

        public long AllocatedAmountMinor { get; init; }
    }
}
