namespace FinancialManagement.Application.Sales;

public sealed record SaleListItem(
    long Id,
    string Reference,
    DateOnly SaleDate,
    long TotalAmountMinor,
    long? ClientId,
    string? ClientName,
    DateOnly? DueDate,
    string? Observation);
