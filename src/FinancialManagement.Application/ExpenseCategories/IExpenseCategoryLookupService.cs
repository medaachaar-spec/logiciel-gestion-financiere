namespace FinancialManagement.Application.ExpenseCategories;

public interface IExpenseCategoryLookupService
{
    Task<ExpenseCategoryLookupResult> GetActiveAsync(
        CancellationToken cancellationToken = default);
}
