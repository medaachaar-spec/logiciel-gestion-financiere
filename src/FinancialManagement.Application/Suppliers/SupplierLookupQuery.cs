namespace FinancialManagement.Application.Suppliers;

public sealed record SupplierLookupQuery(
    string? SearchText = null,
    int Limit = 20);
