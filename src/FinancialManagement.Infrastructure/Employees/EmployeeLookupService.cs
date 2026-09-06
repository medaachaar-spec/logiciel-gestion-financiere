using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Employees;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Employees;

internal sealed class EmployeeLookupService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService) : IEmployeeLookupService
{
    public async Task<EmployeeLookupResult> SearchAsync(
        EmployeeLookupQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return EmployeeLookupResult.Failed(query, EmployeeLookupFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Expenses,
                PermissionAction.Create,
                cancellationToken))
        {
            return EmployeeLookupResult.Failed(query, EmployeeLookupFailureReason.Forbidden);
        }

        if (query.Limit is < 1 or > 50)
        {
            return EmployeeLookupResult.Failed(query, EmployeeLookupFailureReason.InvalidLimit);
        }

        var employees = dbContext.Users
            .AsNoTracking()
            .Where(user => user.IsActive && user.Role == UserRole.Employe);
        var searchText = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(searchText))
        {
            employees = employees.Where(user =>
                user.FullName.Contains(searchText)
                || user.Username.Contains(searchText));
        }

        var items = await employees
            .OrderBy(user => user.FullName)
            .ThenBy(user => user.Id)
            .Take(query.Limit)
            .Select(user => new EmployeeLookupItem(
                user.Id,
                user.FullName,
                user.Username))
            .ToListAsync(cancellationToken);

        return EmployeeLookupResult.Succeeded(query, items);
    }
}
