namespace FinancialManagement.Application.Sales;

public enum SaleListFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPageNumber,
    InvalidPageSize,
    InvalidDateRange
}
