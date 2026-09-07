namespace FinancialManagement.Application.CustomerPayments;

public enum CustomerPaymentListFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPageNumber,
    InvalidPageSize,
    InvalidDateRange,
    InvalidPaymentMethod,
    DataIntegrityViolation
}
