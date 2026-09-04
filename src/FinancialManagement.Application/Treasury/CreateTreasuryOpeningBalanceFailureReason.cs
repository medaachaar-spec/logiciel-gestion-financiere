namespace FinancialManagement.Application.Treasury;

public enum CreateTreasuryOpeningBalanceFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    AlreadyInitialized
}
