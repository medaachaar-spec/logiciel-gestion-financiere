namespace FinancialManagement.Application.Sales;

public enum CreateSaleFailureReason
{
    None,
    NotAuthenticated,
    NotAuthorized,
    InvalidTotalAmount,
    ClientNotFound,
    AnonymousSaleRequiresInitialPayment,
    AnonymousSalePaymentMustMatchTotal,
    InvalidInitialPaymentAmount,
    InvalidPaymentMethod
}
