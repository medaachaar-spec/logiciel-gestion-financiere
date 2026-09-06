using FinancialManagement.Application.ExpenseCategories;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class ExpenseCategoryLookupItemViewModel
{
    public ExpenseCategoryLookupItemViewModel(ExpenseCategoryLookupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        ExpenseCategoryId = item.ExpenseCategoryId;
        DisplayName = item.DisplayName;
    }

    public long ExpenseCategoryId { get; }
    public string DisplayName { get; }
}
