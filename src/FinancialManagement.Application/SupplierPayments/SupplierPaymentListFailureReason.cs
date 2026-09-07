namespace FinancialManagement.Application.SupplierPayments;

public enum SupplierPaymentListFailureReason
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
