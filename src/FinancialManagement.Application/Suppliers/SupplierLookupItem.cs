namespace FinancialManagement.Application.Suppliers;

public sealed record SupplierLookupItem(
    long SupplierId,
    string DisplayName,
    string? Identifier);
