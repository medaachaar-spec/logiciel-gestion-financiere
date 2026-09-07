namespace FinancialManagement.Application.InitialSupplierPayables;

public enum InitialSupplierPayableListFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidPageNumber,
    InvalidPageSize,
    InvalidSupplierId,
    DataIntegrityViolation
}
