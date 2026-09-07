namespace FinancialManagement.Application.InitialSupplierPayables;

public sealed record InitialSupplierPayableListItem(
    long Id, string Reference, long SupplierId, string SupplierName, DateOnly PayableDate,
    long InitialAmountMinor, long OutstandingAmountMinor, DateOnly? DueDate, string? Observation);
