namespace FinancialManagement.Application.CustomerPayments;

public enum CustomerPaymentDetailFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPaymentId,
    NotFound,
    DataIntegrityViolation
}
