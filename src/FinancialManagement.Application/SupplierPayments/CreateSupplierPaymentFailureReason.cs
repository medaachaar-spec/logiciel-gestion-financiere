namespace FinancialManagement.Application.SupplierPayments;

public enum CreateSupplierPaymentFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidSupplier,
    InvalidAmount,
    InvalidPaymentMethod,
    MissingAllocations,
    InvalidAllocation,
    DuplicateAllocationTarget,
    ObligationNotFound,
    ObligationBelongsToDifferentSupplier,
    AllocationExceedsOutstanding,
    AllocationsExceedPayment,
    DataIntegrityViolation
}
