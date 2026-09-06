namespace FinancialManagement.Application.Sales;

public sealed record SaleListQuery(
    int PageNumber = 1,
    int PageSize = 50,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? SearchText = null);
