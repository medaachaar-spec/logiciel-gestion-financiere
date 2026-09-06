namespace FinancialManagement.Application.Purchases;

public enum PurchaseListFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPageNumber,
    InvalidPageSize,
    InvalidDateRange
}
