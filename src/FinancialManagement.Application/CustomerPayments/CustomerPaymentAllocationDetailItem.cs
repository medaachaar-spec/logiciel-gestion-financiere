namespace FinancialManagement.Application.CustomerPayments;

public sealed record CustomerPaymentAllocationDetailItem(
    long Id,
    CustomerPaymentAllocationTargetType TargetType,
    long TargetId,
    string TargetReference,
    long OriginalObligationAmountMinor,
    long AllocatedAmountMinor);
