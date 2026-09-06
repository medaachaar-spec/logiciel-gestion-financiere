namespace FinancialManagement.Application.Expenses;

public enum CreateExpenseFailureReason
{
    None,
    NotAuthenticated,
    NotAuthorized,
    InvalidAmount,
    InvalidPaymentMethod,
    ExpenseCategoryNotFound,
    ExpenseCategoryInactive,
    ConcernedEmployeeNotFound,
    ConcernedEmployeeNotEligible
}
