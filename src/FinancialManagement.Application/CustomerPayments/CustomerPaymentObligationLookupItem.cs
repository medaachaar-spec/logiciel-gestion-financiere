namespace FinancialManagement.Application.CustomerPayments;

public sealed record CustomerPaymentObligationLookupItem(
    CustomerPaymentObligationTargetType TargetType,
    long TargetId,
    string Reference,
    DateOnly BusinessDate,
    DateOnly? DueDate,
    long OriginalAmountMinor,
    long OutstandingAmountMinor);
