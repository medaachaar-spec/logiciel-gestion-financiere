namespace FinancialManagement.Application.Purchases;

public sealed record PurchaseListItem(
    long Id,
    string Reference,
    DateOnly PurchaseDate,
    long TotalAmountMinor,
    long SupplierId,
    string SupplierName,
    DateOnly? DueDate,
    string? Observation);
