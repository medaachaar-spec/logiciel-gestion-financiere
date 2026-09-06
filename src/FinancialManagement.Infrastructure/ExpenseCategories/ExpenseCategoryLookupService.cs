using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.ExpenseCategories;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.ExpenseCategories;

internal sealed class ExpenseCategoryLookupService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService) : IExpenseCategoryLookupService
{
    public async Task<ExpenseCategoryLookupResult> GetActiveAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return ExpenseCategoryLookupResult.Failed(
                ExpenseCategoryLookupFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Expenses,
                PermissionAction.Create,
                cancellationToken))
        {
            return ExpenseCategoryLookupResult.Failed(
                ExpenseCategoryLookupFailureReason.Forbidden);
        }

        var items = await dbContext.ExpenseCategories
            .AsNoTracking()
            .Where(category => category.IsActive)
            .OrderBy(category => category.Name)
            .ThenBy(category => category.Id)
            .Select(category => new ExpenseCategoryLookupItem(
                category.Id,
                category.Name))
            .ToListAsync(cancellationToken);

        return ExpenseCategoryLookupResult.Succeeded(items);
    }
}
