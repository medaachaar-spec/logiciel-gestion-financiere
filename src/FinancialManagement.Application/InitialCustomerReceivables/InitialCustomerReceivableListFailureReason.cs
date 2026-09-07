namespace FinancialManagement.Application.InitialCustomerReceivables;

public enum InitialCustomerReceivableListFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPageNumber,
    InvalidPageSize,
    InvalidClientId,
    DataIntegrityViolation
}
