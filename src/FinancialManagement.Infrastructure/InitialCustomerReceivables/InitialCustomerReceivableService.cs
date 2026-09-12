using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.InitialCustomerReceivables;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.InitialCustomerReceivables;

internal sealed class InitialCustomerReceivableService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    IBusinessReferenceGenerator referenceGenerator,
    TimeProvider timeProvider) : IInitialCustomerReceivableService
{
    public async Task<InitialCustomerReceivableListResult> GetPageAsync(
        InitialCustomerReceivableListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is null)
            return InitialCustomerReceivableListResult.Failed(query, InitialCustomerReceivableListFailureReason.NotAuthenticated);
        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Administration,
                cancellationToken))
            return InitialCustomerReceivableListResult.Failed(query, InitialCustomerReceivableListFailureReason.Forbidden);
        if (query.PageNumber < 1 || ((long)query.PageNumber - 1) * query.PageSize > int.MaxValue)
            return InitialCustomerReceivableListResult.Failed(query, InitialCustomerReceivableListFailureReason.InvalidPageNumber);
        if (query.PageSize is < 1 or > 200)
            return InitialCustomerReceivableListResult.Failed(query, InitialCustomerReceivableListFailureReason.InvalidPageSize);
        if (query.ClientId is <= 0)
            return InitialCustomerReceivableListResult.Failed(query, InitialCustomerReceivableListFailureReason.InvalidClientId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Administration,
                cancellationToken))
            return InitialCustomerReceivableListResult.Failed(
                query,
                InitialCustomerReceivableListFailureReason.Forbidden);
        var obligations = dbContext.InitialCustomerReceivables.AsNoTracking();
        if (query.ClientId.HasValue)
            obligations = obligations.Where(item => item.ClientId == query.ClientId.Value);
        var totalCount = await obligations.CountAsync(cancellationToken);
        var rows = await (from obligation in obligations
                          join party in dbContext.Clients.AsNoTracking() on obligation.ClientId equals party.Id
                          orderby obligation.ReceivableDate descending, obligation.Id descending
                          select new { obligation, PartyName = party.Name })
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        var ids = rows.Select(row => row.obligation.Id).ToArray();
        var allocations = await dbContext.CustomerPaymentAllocations.AsNoTracking()
            .Where(item => item.InitialCustomerReceivableId.HasValue && ids.Contains(item.InitialCustomerReceivableId.Value))
            .Select(item => new { TargetId = item.InitialCustomerReceivableId!.Value, item.AmountMinor })
            .ToListAsync(cancellationToken);
        var totals = new Dictionary<long, long>();
        try
        {
            foreach (var allocation in allocations)
            {
                if (allocation.AmountMinor <= 0)
                    return InitialCustomerReceivableListResult.Failed(query, InitialCustomerReceivableListFailureReason.DataIntegrityViolation);
                totals[allocation.TargetId] = checked(totals.GetValueOrDefault(allocation.TargetId) + allocation.AmountMinor);
            }
        }
        catch (OverflowException)
        {
            return InitialCustomerReceivableListResult.Failed(query, InitialCustomerReceivableListFailureReason.DataIntegrityViolation);
        }

        var items = new List<InitialCustomerReceivableListItem>();
        foreach (var row in rows)
        {
            var obligation = row.obligation;
            var allocated = totals.GetValueOrDefault(obligation.Id);
            if (obligation.InitialAmountMinor <= 0 || allocated > obligation.InitialAmountMinor)
                return InitialCustomerReceivableListResult.Failed(query, InitialCustomerReceivableListFailureReason.DataIntegrityViolation);
            items.Add(new(obligation.Id, obligation.Reference, obligation.ClientId, row.PartyName,
                obligation.ReceivableDate, obligation.InitialAmountMinor, obligation.InitialAmountMinor - allocated,
                obligation.DueDate, obligation.Observation));
        }
        await transaction.CommitAsync(cancellationToken);
        return InitialCustomerReceivableListResult.Succeeded(query, items, totalCount);
    }

    public async Task<CreateInitialCustomerReceivableResult> CreateAsync(
        CreateInitialCustomerReceivableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is not long userId)
        {
            return CreateInitialCustomerReceivableResult.Failed(
                CreateInitialCustomerReceivableFailureReason.NotAuthenticated,
                command.InitialAmountMinor);
        }

        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Administration,
                cancellationToken))
        {
            return CreateInitialCustomerReceivableResult.Failed(
                CreateInitialCustomerReceivableFailureReason.Forbidden,
                command.InitialAmountMinor);
        }

        if (command.ClientId <= 0)
        {
            return CreateInitialCustomerReceivableResult.Failed(
                CreateInitialCustomerReceivableFailureReason.InvalidClient,
                command.InitialAmountMinor);
        }

        if (command.InitialAmountMinor <= 0)
        {
            return CreateInitialCustomerReceivableResult.Failed(
                CreateInitialCustomerReceivableFailureReason.InvalidAmount,
                command.InitialAmountMinor);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Administration,
                cancellationToken)
            || currentUserSession.UserId != userId)
        {
            return CreateInitialCustomerReceivableResult.Failed(
                CreateInitialCustomerReceivableFailureReason.Forbidden,
                command.InitialAmountMinor);
        }

        var clientExists = await dbContext.Clients
            .AsNoTracking()
            .AnyAsync(client => client.Id == command.ClientId, cancellationToken);

        if (!clientExists)
        {
            return CreateInitialCustomerReceivableResult.Failed(
                CreateInitialCustomerReceivableFailureReason.InvalidClient,
                command.InitialAmountMinor);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var receivable = new InitialCustomerReceivable
        {
            Reference = referenceGenerator.GenerateInitialCustomerReceivableReference(
                command.ReceivableDate),
            ClientId = command.ClientId,
            ReceivableDate = command.ReceivableDate,
            InitialAmountMinor = command.InitialAmountMinor,
            DueDate = command.DueDate,
            Observation = command.Observation,
            CreatedByUserId = userId,
            CreatedAtUtc = createdAtUtc
        };

        dbContext.InitialCustomerReceivables.Add(receivable);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateInitialCustomerReceivableResult.Succeeded(
            receivable.Id,
            receivable.Reference,
            receivable.InitialAmountMinor);
    }
}
