using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Suppliers;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Suppliers;

internal sealed class SupplierLookupService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService) : ISupplierLookupService
{
    public async Task<SupplierLookupResult> SearchAsync(
        SupplierLookupQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return SupplierLookupResult.Failed(query, SupplierLookupFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Suppliers,
                PermissionAction.View,
                cancellationToken))
        {
            return SupplierLookupResult.Failed(query, SupplierLookupFailureReason.Forbidden);
        }

        if (query.Limit is < 1 or > 50)
        {
            return SupplierLookupResult.Failed(query, SupplierLookupFailureReason.InvalidLimit);
        }

        var suppliers = dbContext.Suppliers.AsNoTracking();
        var searchText = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(searchText))
        {
            suppliers = suppliers.Where(supplier =>
                supplier.Name.Contains(searchText)
                || (supplier.Identifier != null && supplier.Identifier.Contains(searchText)));
        }

        var items = await suppliers
            .OrderBy(supplier => supplier.Name)
            .ThenBy(supplier => supplier.Id)
            .Take(query.Limit)
            .Select(supplier => new SupplierLookupItem(
                supplier.Id,
                supplier.Name,
                supplier.Identifier))
            .ToListAsync(cancellationToken);

        return SupplierLookupResult.Succeeded(query, items);
    }
}
