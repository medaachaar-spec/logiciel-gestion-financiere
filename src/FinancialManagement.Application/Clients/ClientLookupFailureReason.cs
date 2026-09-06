namespace FinancialManagement.Application.Clients;

public enum ClientLookupFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidLimit
}
