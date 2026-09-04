namespace FinancialManagement.Application.Treasury;

public enum CreateTreasuryTransferFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidAmount,
    InvalidAccount,
    SameAccount
}
