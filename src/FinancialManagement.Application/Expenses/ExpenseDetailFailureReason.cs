namespace FinancialManagement.Application.Expenses;

public enum ExpenseDetailFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidExpenseId,
    NotFound
}
