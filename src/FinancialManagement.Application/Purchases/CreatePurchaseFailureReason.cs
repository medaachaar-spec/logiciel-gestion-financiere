namespace FinancialManagement.Application.Purchases;

public enum CreatePurchaseFailureReason
{
    None,
    NotAuthenticated,
    NotAuthorized,
    InvalidTotalAmount,
    SupplierNotFound,
    InvalidInitialPaymentAmount,
    InvalidPaymentMethod
}
