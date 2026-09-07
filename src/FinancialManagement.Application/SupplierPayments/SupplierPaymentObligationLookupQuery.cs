namespace FinancialManagement.Application.SupplierPayments;

public sealed record SupplierPaymentObligationLookupQuery(
    long SupplierId,
    SupplierPaymentObligationLookupPurpose Purpose,
    string? SearchText = null,
    int Limit = 50);
