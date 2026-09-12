using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Suppliers;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Suppliers;

internal sealed class SupplierService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    TimeProvider timeProvider) : ISupplierService
{
    public async Task<CreateSupplierResult> CreateAsync(
        CreateSupplierCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return CreateSupplierResult.Failed(CreateSupplierFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Suppliers,
                PermissionAction.Create,
                cancellationToken))
        {
            return CreateSupplierResult.Failed(CreateSupplierFailureReason.NotAuthorized);
        }

        var name = command.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return CreateSupplierResult.Failed(CreateSupplierFailureReason.InvalidName);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await authorizationService.CanAsync(
                PermissionModule.Suppliers,
                PermissionAction.Create,
                cancellationToken))
        {
            return CreateSupplierResult.Failed(CreateSupplierFailureReason.NotAuthorized);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var supplier = new Supplier
        {
            Name = name,
            Phone = NormalizeOptional(command.Phone),
            Address = NormalizeOptional(command.Address),
            Company = NormalizeOptional(command.Company),
            Identifier = NormalizeOptional(command.Identifier),
            Observation = NormalizeOptional(command.Observation),
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

        dbContext.Suppliers.Add(supplier);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateSupplierResult.Succeeded(supplier.Id);
    }

    public async Task<SupplierListResult> GetPageAsync(
        SupplierListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return SupplierListResult.Failed(query, SupplierListFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Suppliers,
                PermissionAction.View,
                cancellationToken))
        {
            return SupplierListResult.Failed(query, SupplierListFailureReason.Forbidden);
        }

        if (query.PageNumber < 1)
        {
            return SupplierListResult.Failed(query, SupplierListFailureReason.InvalidPageNumber);
        }

        if (query.PageSize is < 1 or > 200)
        {
            return SupplierListResult.Failed(query, SupplierListFailureReason.InvalidPageSize);
        }

        var skip = ((long)query.PageNumber - 1) * query.PageSize;
        if (skip > int.MaxValue)
        {
            return SupplierListResult.Failed(query, SupplierListFailureReason.InvalidPageNumber);
        }

        var suppliers = dbContext.Suppliers.AsNoTracking();
        var searchText = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(searchText))
        {
            suppliers = suppliers.Where(supplier =>
                supplier.Name.Contains(searchText)
                || (supplier.Identifier != null && supplier.Identifier.Contains(searchText)));
        }

        var totalCount = await suppliers.CountAsync(cancellationToken);
        var rows = await suppliers
            .OrderBy(supplier => supplier.Name)
            .ThenBy(supplier => supplier.Id)
            .Skip((int)skip)
            .Take(query.PageSize)
            .Select(supplier => new
            {
                supplier.Id,
                supplier.Name,
                supplier.Phone,
                supplier.Company,
                supplier.Identifier
            })
            .ToListAsync(cancellationToken);
        var balances = await CurrentPartyBalanceReader.ReadSupplierAsync(
            dbContext,
            rows.Select(item => item.Id).ToArray(),
            cancellationToken);
        if (balances is null)
        {
            return SupplierListResult.Failed(query, SupplierListFailureReason.DataIntegrityViolation);
        }

        var items = rows
            .Select(supplier => new SupplierListItem(
                supplier.Id,
                supplier.Name,
                supplier.Phone,
                supplier.Company,
                supplier.Identifier,
                balances.GetValueOrDefault(supplier.Id)))
            .ToArray();

        return SupplierListResult.Succeeded(query, items, totalCount);
    }

    public async Task<SupplierDetailResult> GetByIdAsync(
        long supplierId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return SupplierDetailResult.Failed(SupplierDetailFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Suppliers,
                PermissionAction.View,
                cancellationToken))
        {
            return SupplierDetailResult.Failed(SupplierDetailFailureReason.Forbidden);
        }

        if (supplierId <= 0)
        {
            return SupplierDetailResult.Failed(SupplierDetailFailureReason.InvalidSupplierId);
        }

        var item = await dbContext.Suppliers
            .AsNoTracking()
            .Where(supplier => supplier.Id == supplierId)
            .Select(supplier => new SupplierDetailItem(
                supplier.Id,
                supplier.Name,
                supplier.Phone,
                supplier.Address,
                supplier.Company,
                supplier.Identifier,
                supplier.Observation,
                supplier.CreatedAtUtc,
                supplier.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return item is null
            ? SupplierDetailResult.Failed(SupplierDetailFailureReason.NotFound)
            : SupplierDetailResult.Succeeded(item);
    }

    public async Task<UpdateSupplierResult> UpdateAsync(
        UpdateSupplierCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return UpdateSupplierResult.Failed(UpdateSupplierFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Suppliers,
                PermissionAction.Modify,
                cancellationToken))
        {
            return UpdateSupplierResult.Failed(UpdateSupplierFailureReason.NotAuthorized);
        }

        if (command.SupplierId <= 0)
        {
            return UpdateSupplierResult.Failed(UpdateSupplierFailureReason.InvalidSupplierId);
        }

        var name = command.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return UpdateSupplierResult.Failed(UpdateSupplierFailureReason.InvalidName);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await authorizationService.CanAsync(
                PermissionModule.Suppliers,
                PermissionAction.Modify,
                cancellationToken))
        {
            return UpdateSupplierResult.Failed(UpdateSupplierFailureReason.NotAuthorized);
        }

        var supplier = await dbContext.Suppliers
            .SingleOrDefaultAsync(item => item.Id == command.SupplierId, cancellationToken);
        if (supplier is null)
        {
            return UpdateSupplierResult.Failed(UpdateSupplierFailureReason.NotFound);
        }

        supplier.Name = name;
        supplier.Phone = NormalizeOptional(command.Phone);
        supplier.Address = NormalizeOptional(command.Address);
        supplier.Company = NormalizeOptional(command.Company);
        supplier.Identifier = NormalizeOptional(command.Identifier);
        supplier.Observation = NormalizeOptional(command.Observation);
        supplier.UpdatedAtUtc = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return UpdateSupplierResult.Succeeded(supplier.Id);
    }

    public async Task<DeleteSupplierResult> DeleteAsync(
        long supplierId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return DeleteSupplierResult.Failed(DeleteSupplierFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.PermanentDelete,
                cancellationToken))
        {
            return DeleteSupplierResult.Failed(DeleteSupplierFailureReason.NotAuthorized);
        }

        if (supplierId <= 0)
        {
            return DeleteSupplierResult.Failed(DeleteSupplierFailureReason.InvalidSupplierId);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.PermanentDelete,
                cancellationToken))
        {
            return DeleteSupplierResult.Failed(DeleteSupplierFailureReason.NotAuthorized);
        }
        var supplier = await dbContext.Suppliers
            .SingleOrDefaultAsync(item => item.Id == supplierId, cancellationToken);
        if (supplier is null)
        {
            return DeleteSupplierResult.Failed(DeleteSupplierFailureReason.NotFound);
        }

        var inUse = await dbContext.Purchases
            .AsNoTracking()
            .AnyAsync(purchase => purchase.SupplierId == supplierId, cancellationToken)
            || await dbContext.SupplierPayments
                .AsNoTracking()
                .AnyAsync(payment => payment.SupplierId == supplierId, cancellationToken)
            || await dbContext.InitialSupplierPayables
                .AsNoTracking()
                .AnyAsync(payable => payable.SupplierId == supplierId, cancellationToken);

        if (inUse)
        {
            return DeleteSupplierResult.Failed(DeleteSupplierFailureReason.InUse);
        }

        dbContext.Suppliers.Remove(supplier);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsForeignKeyConstraintViolation(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.Entry(supplier).State = EntityState.Unchanged;
            return DeleteSupplierResult.Failed(DeleteSupplierFailureReason.InUse);
        }

        return DeleteSupplierResult.Succeeded(supplierId);
    }

    private static string? NormalizeOptional(string? value)
    {
        var normalized = value?.Trim();
        return string.IsNullOrEmpty(normalized) ? null : normalized;
    }

    private static bool IsForeignKeyConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is SqliteException
        {
            SqliteErrorCode: 19,
            SqliteExtendedErrorCode: 787
        };
}
