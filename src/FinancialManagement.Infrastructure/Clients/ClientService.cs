using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Clients;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Clients;

internal sealed class ClientService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    TimeProvider timeProvider) : IClientService
{
    public async Task<CreateClientResult> CreateAsync(
        CreateClientCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return CreateClientResult.Failed(CreateClientFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Clients,
                PermissionAction.Create,
                cancellationToken))
        {
            return CreateClientResult.Failed(CreateClientFailureReason.NotAuthorized);
        }

        var name = command.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return CreateClientResult.Failed(CreateClientFailureReason.InvalidName);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var client = new Client
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

        dbContext.Clients.Add(client);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateClientResult.Succeeded(client.Id);
    }

    public async Task<ClientListResult> GetPageAsync(
        ClientListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return ClientListResult.Failed(query, ClientListFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Clients,
                PermissionAction.View,
                cancellationToken))
        {
            return ClientListResult.Failed(query, ClientListFailureReason.Forbidden);
        }

        if (query.PageNumber < 1)
        {
            return ClientListResult.Failed(query, ClientListFailureReason.InvalidPageNumber);
        }

        if (query.PageSize is < 1 or > 200)
        {
            return ClientListResult.Failed(query, ClientListFailureReason.InvalidPageSize);
        }

        var skip = ((long)query.PageNumber - 1) * query.PageSize;
        if (skip > int.MaxValue)
        {
            return ClientListResult.Failed(query, ClientListFailureReason.InvalidPageNumber);
        }

        var clients = dbContext.Clients.AsNoTracking();
        var searchText = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(searchText))
        {
            clients = clients.Where(client =>
                client.Name.Contains(searchText)
                || (client.Identifier != null && client.Identifier.Contains(searchText)));
        }

        var totalCount = await clients.CountAsync(cancellationToken);
        var rows = await clients
            .OrderBy(client => client.Name)
            .ThenBy(client => client.Id)
            .Skip((int)skip)
            .Take(query.PageSize)
            .Select(client => new
            {
                client.Id,
                client.Name,
                client.Phone,
                client.Company,
                client.Identifier
            })
            .ToListAsync(cancellationToken);
        var balances = await CurrentPartyBalanceReader.ReadCustomerAsync(
            dbContext,
            rows.Select(item => item.Id).ToArray(),
            cancellationToken);
        if (balances is null)
        {
            return ClientListResult.Failed(query, ClientListFailureReason.DataIntegrityViolation);
        }

        var items = rows
            .Select(client => new ClientListItem(
                client.Id,
                client.Name,
                client.Phone,
                client.Company,
                client.Identifier,
                balances.GetValueOrDefault(client.Id)))
            .ToArray();

        return ClientListResult.Succeeded(query, items, totalCount);
    }

    public async Task<ClientDetailResult> GetByIdAsync(
        long clientId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return ClientDetailResult.Failed(ClientDetailFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Clients,
                PermissionAction.View,
                cancellationToken))
        {
            return ClientDetailResult.Failed(ClientDetailFailureReason.Forbidden);
        }

        if (clientId <= 0)
        {
            return ClientDetailResult.Failed(ClientDetailFailureReason.InvalidClientId);
        }

        var item = await dbContext.Clients
            .AsNoTracking()
            .Where(client => client.Id == clientId)
            .Select(client => new ClientDetailItem(
                client.Id,
                client.Name,
                client.Phone,
                client.Address,
                client.Company,
                client.Identifier,
                client.Observation,
                client.CreatedAtUtc,
                client.UpdatedAtUtc))
            .SingleOrDefaultAsync(cancellationToken);

        return item is null
            ? ClientDetailResult.Failed(ClientDetailFailureReason.NotFound)
            : ClientDetailResult.Succeeded(item);
    }

    public async Task<UpdateClientResult> UpdateAsync(
        UpdateClientCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return UpdateClientResult.Failed(UpdateClientFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Clients,
                PermissionAction.Modify,
                cancellationToken))
        {
            return UpdateClientResult.Failed(UpdateClientFailureReason.NotAuthorized);
        }

        if (command.ClientId <= 0)
        {
            return UpdateClientResult.Failed(UpdateClientFailureReason.InvalidClientId);
        }

        var name = command.Name?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return UpdateClientResult.Failed(UpdateClientFailureReason.InvalidName);
        }

        var client = await dbContext.Clients
            .SingleOrDefaultAsync(item => item.Id == command.ClientId, cancellationToken);
        if (client is null)
        {
            return UpdateClientResult.Failed(UpdateClientFailureReason.NotFound);
        }

        client.Name = name;
        client.Phone = NormalizeOptional(command.Phone);
        client.Address = NormalizeOptional(command.Address);
        client.Company = NormalizeOptional(command.Company);
        client.Identifier = NormalizeOptional(command.Identifier);
        client.Observation = NormalizeOptional(command.Observation);
        client.UpdatedAtUtc = timeProvider.GetUtcNow();

        await dbContext.SaveChangesAsync(cancellationToken);

        return UpdateClientResult.Succeeded(client.Id);
    }

    public async Task<DeleteClientResult> DeleteAsync(
        long clientId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return DeleteClientResult.Failed(DeleteClientFailureReason.NotAuthenticated);
        }

        if (!authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.PermanentDelete))
        {
            return DeleteClientResult.Failed(DeleteClientFailureReason.NotAuthorized);
        }

        if (clientId <= 0)
        {
            return DeleteClientResult.Failed(DeleteClientFailureReason.InvalidClientId);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var client = await dbContext.Clients
            .SingleOrDefaultAsync(item => item.Id == clientId, cancellationToken);
        if (client is null)
        {
            return DeleteClientResult.Failed(DeleteClientFailureReason.NotFound);
        }

        var inUse = await dbContext.Sales
            .AsNoTracking()
            .AnyAsync(sale => sale.ClientId == clientId, cancellationToken)
            || await dbContext.CustomerPayments
                .AsNoTracking()
                .AnyAsync(payment => payment.ClientId == clientId, cancellationToken)
            || await dbContext.InitialCustomerReceivables
                .AsNoTracking()
                .AnyAsync(receivable => receivable.ClientId == clientId, cancellationToken);

        if (inUse)
        {
            return DeleteClientResult.Failed(DeleteClientFailureReason.InUse);
        }

        dbContext.Clients.Remove(client);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsForeignKeyConstraintViolation(exception))
        {
            await transaction.RollbackAsync(CancellationToken.None);
            dbContext.Entry(client).State = EntityState.Unchanged;
            return DeleteClientResult.Failed(DeleteClientFailureReason.InUse);
        }

        return DeleteClientResult.Succeeded(clientId);
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
