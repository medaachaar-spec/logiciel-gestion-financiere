namespace FinancialManagement.Application.CustomerPayments;

public enum CreateCustomerPaymentFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidClient,
    InvalidAmount,
    InvalidPaymentMethod,
    MissingAllocations,
    InvalidAllocation,
    DuplicateAllocationTarget,
    ObligationNotFound,
    ObligationBelongsToDifferentClient,
    AllocationExceedsOutstanding,
    AllocationsExceedPayment
}
