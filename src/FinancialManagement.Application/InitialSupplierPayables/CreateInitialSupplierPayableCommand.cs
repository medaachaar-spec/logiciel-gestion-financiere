namespace FinancialManagement.Application.InitialSupplierPayables;

public sealed record CreateInitialSupplierPayableCommand(
    long SupplierId,
    long InitialAmountMinor,
    DateOnly PayableDate,
    DateOnly? DueDate,
    string? Observation);
