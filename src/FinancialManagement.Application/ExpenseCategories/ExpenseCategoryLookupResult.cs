namespace FinancialManagement.Application.ExpenseCategories;

public sealed class ExpenseCategoryLookupResult
{
    private ExpenseCategoryLookupResult(
        bool success,
        ExpenseCategoryLookupFailureReason failureReason,
        IReadOnlyList<ExpenseCategoryLookupItem> items)
    {
        Success = success;
        FailureReason = failureReason;
        Items = items;
    }

    public bool Success { get; }

    public ExpenseCategoryLookupFailureReason FailureReason { get; }

    public IReadOnlyList<ExpenseCategoryLookupItem> Items { get; }

    public static ExpenseCategoryLookupResult Failed(
        ExpenseCategoryLookupFailureReason failureReason) =>
        new(false, failureReason, Array.Empty<ExpenseCategoryLookupItem>());

    public static ExpenseCategoryLookupResult Succeeded(
        IReadOnlyList<ExpenseCategoryLookupItem> items) =>
        new(true, ExpenseCategoryLookupFailureReason.None, items);
}
