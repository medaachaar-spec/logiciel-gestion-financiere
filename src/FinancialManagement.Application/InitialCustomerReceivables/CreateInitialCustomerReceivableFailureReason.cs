namespace FinancialManagement.Application.InitialCustomerReceivables;

public enum CreateInitialCustomerReceivableFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidClient,
    InvalidAmount
}
