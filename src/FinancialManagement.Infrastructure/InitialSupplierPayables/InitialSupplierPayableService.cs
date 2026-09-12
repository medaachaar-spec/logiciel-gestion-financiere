using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.InitialSupplierPayables;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.InitialSupplierPayables;

internal sealed class InitialSupplierPayableService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    IBusinessReferenceGenerator referenceGenerator,
    TimeProvider timeProvider) : IInitialSupplierPayableService
{
    public async Task<InitialSupplierPayableListResult> GetPageAsync(
        InitialSupplierPayableListQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();
        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is null)
            return InitialSupplierPayableListResult.Failed(query, InitialSupplierPayableListFailureReason.NotAuthenticated);
        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Administration,
                cancellationToken))
            return InitialSupplierPayableListResult.Failed(query, InitialSupplierPayableListFailureReason.Forbidden);
        if (query.PageNumber < 1 || ((long)query.PageNumber - 1) * query.PageSize > int.MaxValue)
            return InitialSupplierPayableListResult.Failed(query, InitialSupplierPayableListFailureReason.InvalidPageNumber);
        if (query.PageSize is < 1 or > 200)
            return InitialSupplierPayableListResult.Failed(query, InitialSupplierPayableListFailureReason.InvalidPageSize);
        if (query.SupplierId is <= 0)
            return InitialSupplierPayableListResult.Failed(query, InitialSupplierPayableListFailureReason.InvalidSupplierId);

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Administration,
                cancellationToken))
            return InitialSupplierPayableListResult.Failed(
                query,
                InitialSupplierPayableListFailureReason.Forbidden);
        var obligations = dbContext.InitialSupplierPayables.AsNoTracking();
        if (query.SupplierId.HasValue)
            obligations = obligations.Where(item => item.SupplierId == query.SupplierId.Value);
        var totalCount = await obligations.CountAsync(cancellationToken);
        var rows = await (from obligation in obligations
                          join party in dbContext.Suppliers.AsNoTracking() on obligation.SupplierId equals party.Id
                          orderby obligation.PayableDate descending, obligation.Id descending
                          select new { obligation, PartyName = party.Name })
            .Skip((query.PageNumber - 1) * query.PageSize).Take(query.PageSize).ToListAsync(cancellationToken);
        var ids = rows.Select(row => row.obligation.Id).ToArray();
        var allocations = await dbContext.SupplierPaymentAllocations.AsNoTracking()
            .Where(item => item.InitialSupplierPayableId.HasValue && ids.Contains(item.InitialSupplierPayableId.Value))
            .Select(item => new { TargetId = item.InitialSupplierPayableId!.Value, item.AmountMinor })
            .ToListAsync(cancellationToken);
        var totals = new Dictionary<long, long>();
        try
        {
            foreach (var allocation in allocations)
            {
                if (allocation.AmountMinor <= 0)
                    return InitialSupplierPayableListResult.Failed(query, InitialSupplierPayableListFailureReason.DataIntegrityViolation);
                totals[allocation.TargetId] = checked(totals.GetValueOrDefault(allocation.TargetId) + allocation.AmountMinor);
            }
        }
        catch (OverflowException)
        {
            return InitialSupplierPayableListResult.Failed(query, InitialSupplierPayableListFailureReason.DataIntegrityViolation);
        }

        var items = new List<InitialSupplierPayableListItem>();
        foreach (var row in rows)
        {
            var obligation = row.obligation;
            var allocated = totals.GetValueOrDefault(obligation.Id);
            if (obligation.InitialAmountMinor <= 0 || allocated > obligation.InitialAmountMinor)
                return InitialSupplierPayableListResult.Failed(query, InitialSupplierPayableListFailureReason.DataIntegrityViolation);
            items.Add(new(obligation.Id, obligation.Reference, obligation.SupplierId, row.PartyName,
                obligation.PayableDate, obligation.InitialAmountMinor, obligation.InitialAmountMinor - allocated,
                obligation.DueDate, obligation.Observation));
        }
        await transaction.CommitAsync(cancellationToken);
        return InitialSupplierPayableListResult.Succeeded(query, items, totalCount);
    }

    public async Task<CreateInitialSupplierPayableResult> CreateAsync(
        CreateInitialSupplierPayableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is not long userId)
        {
            return CreateInitialSupplierPayableResult.Failed(
                CreateInitialSupplierPayableFailureReason.NotAuthenticated,
                command.InitialAmountMinor);
        }

        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Administration,
                cancellationToken))
        {
            return CreateInitialSupplierPayableResult.Failed(
                CreateInitialSupplierPayableFailureReason.Forbidden,
                command.InitialAmountMinor);
        }

        if (command.SupplierId <= 0)
        {
            return CreateInitialSupplierPayableResult.Failed(
                CreateInitialSupplierPayableFailureReason.InvalidSupplier,
                command.InitialAmountMinor);
        }

        if (command.InitialAmountMinor <= 0)
        {
            return CreateInitialSupplierPayableResult.Failed(
                CreateInitialSupplierPayableFailureReason.InvalidAmount,
                command.InitialAmountMinor);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Administration,
                cancellationToken)
            || currentUserSession.UserId != userId)
        {
            return CreateInitialSupplierPayableResult.Failed(
                CreateInitialSupplierPayableFailureReason.Forbidden,
                command.InitialAmountMinor);
        }

        var supplierExists = await dbContext.Suppliers
            .AsNoTracking()
            .AnyAsync(supplier => supplier.Id == command.SupplierId, cancellationToken);

        if (!supplierExists)
        {
            return CreateInitialSupplierPayableResult.Failed(
                CreateInitialSupplierPayableFailureReason.InvalidSupplier,
                command.InitialAmountMinor);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var payable = new InitialSupplierPayable
        {
            Reference = referenceGenerator.GenerateInitialSupplierPayableReference(
                command.PayableDate),
            SupplierId = command.SupplierId,
            PayableDate = command.PayableDate,
            InitialAmountMinor = command.InitialAmountMinor,
            DueDate = command.DueDate,
            Observation = command.Observation,
            CreatedByUserId = userId,
            CreatedAtUtc = createdAtUtc
        };

        dbContext.InitialSupplierPayables.Add(payable);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateInitialSupplierPayableResult.Succeeded(
            payable.Id,
            payable.Reference,
            payable.InitialAmountMinor);
    }
}
