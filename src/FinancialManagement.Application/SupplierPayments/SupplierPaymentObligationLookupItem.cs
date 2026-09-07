namespace FinancialManagement.Application.SupplierPayments;

public sealed record SupplierPaymentObligationLookupItem(
    SupplierPaymentObligationTargetType TargetType,
    long TargetId,
    string Reference,
    DateOnly BusinessDate,
    DateOnly? DueDate,
    long OriginalAmountMinor,
    long OutstandingAmountMinor);
