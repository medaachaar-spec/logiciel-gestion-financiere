namespace FinancialManagement.Application.Purchases;

public sealed record PurchaseListQuery(
    int PageNumber = 1,
    int PageSize = 50,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? SearchText = null);
