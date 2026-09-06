using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Clients;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Clients;

internal sealed class ClientLookupService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService) : IClientLookupService
{
    public async Task<ClientLookupResult> SearchAsync(
        ClientLookupQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return ClientLookupResult.Failed(query, ClientLookupFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Clients,
                PermissionAction.View,
                cancellationToken))
        {
            return ClientLookupResult.Failed(query, ClientLookupFailureReason.Forbidden);
        }

        if (query.Limit is < 1 or > 50)
        {
            return ClientLookupResult.Failed(query, ClientLookupFailureReason.InvalidLimit);
        }

        var clients = dbContext.Clients.AsNoTracking();
        var searchText = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(searchText))
        {
            clients = clients.Where(client =>
                client.Name.Contains(searchText)
                || (client.Identifier != null && client.Identifier.Contains(searchText)));
        }

        var items = await clients
            .OrderBy(client => client.Name)
            .ThenBy(client => client.Id)
            .Take(query.Limit)
            .Select(client => new ClientLookupItem(
                client.Id,
                client.Name,
                client.Identifier))
            .ToListAsync(cancellationToken);

        return ClientLookupResult.Succeeded(query, items);
    }
}
