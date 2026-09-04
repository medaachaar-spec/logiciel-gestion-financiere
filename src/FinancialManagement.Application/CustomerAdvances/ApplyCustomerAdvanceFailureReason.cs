namespace FinancialManagement.Application.CustomerAdvances;

public enum ApplyCustomerAdvanceFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidSourcePayment,
    SourcePaymentNotFound,
    SourcePaymentNotEligible,
    NoAvailableAdvance,
    MissingAllocations,
    InvalidAllocation,
    DuplicateAllocationTarget,
    ObligationNotFound,
    ObligationBelongsToDifferentClient,
    AllocationExceedsOutstanding,
    AllocationsExceedAvailableAdvance
}
