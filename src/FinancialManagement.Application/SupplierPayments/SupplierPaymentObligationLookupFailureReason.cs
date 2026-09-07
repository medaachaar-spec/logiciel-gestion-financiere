namespace FinancialManagement.Application.SupplierPayments;

public enum SupplierPaymentObligationLookupFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPurpose,
    InvalidSupplierId,
    SupplierNotFound,
    InvalidLimit,
    DataIntegrityViolation
}
