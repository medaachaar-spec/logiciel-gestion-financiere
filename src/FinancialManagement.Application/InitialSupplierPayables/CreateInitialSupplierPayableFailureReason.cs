namespace FinancialManagement.Application.InitialSupplierPayables;

public enum CreateInitialSupplierPayableFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidSupplier,
    InvalidAmount
}
