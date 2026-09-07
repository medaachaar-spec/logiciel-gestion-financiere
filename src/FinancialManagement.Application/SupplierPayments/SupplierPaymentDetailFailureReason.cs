namespace FinancialManagement.Application.SupplierPayments;

public enum SupplierPaymentDetailFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPaymentId,
    NotFound,
    DataIntegrityViolation
}
