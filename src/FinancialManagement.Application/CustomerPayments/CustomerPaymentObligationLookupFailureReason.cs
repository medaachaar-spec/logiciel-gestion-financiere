namespace FinancialManagement.Application.CustomerPayments;

public enum CustomerPaymentObligationLookupFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPurpose,
    InvalidClientId,
    ClientNotFound,
    InvalidLimit,
    DataIntegrityViolation
}
