namespace FinancialManagement.Application.SupplierAdvances;

public enum ApplySupplierAdvanceFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidSourcePayment,
    SourcePaymentNotFound,
    NoAvailableAdvance,
    MissingAllocations,
    InvalidAllocation,
    DuplicateAllocationTarget,
    ObligationNotFound,
    ObligationBelongsToDifferentSupplier,
    AllocationExceedsOutstanding,
    AllocationsExceedAvailableAdvance
}
