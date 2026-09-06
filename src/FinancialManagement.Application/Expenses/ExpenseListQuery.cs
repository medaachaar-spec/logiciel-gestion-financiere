namespace FinancialManagement.Application.Expenses;

public sealed record ExpenseListQuery(
    int PageNumber = 1,
    int PageSize = 50,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? SearchText = null);
