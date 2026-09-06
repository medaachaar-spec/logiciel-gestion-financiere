namespace FinancialManagement.Application.Expenses;

public enum ExpenseListFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPageNumber,
    InvalidPageSize,
    InvalidDateRange
}
