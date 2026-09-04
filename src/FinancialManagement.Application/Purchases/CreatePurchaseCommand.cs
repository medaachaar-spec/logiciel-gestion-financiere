namespace FinancialManagement.Application.Purchases;

public sealed record CreatePurchaseCommand(
    long SupplierId,
    long TotalAmountMinor,
    string? Observation,
    DateOnly? DueDate,
    InitialSupplierPaymentInput? InitialPayment);
