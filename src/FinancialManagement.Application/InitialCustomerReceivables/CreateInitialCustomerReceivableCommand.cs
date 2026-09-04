namespace FinancialManagement.Application.InitialCustomerReceivables;

public sealed record CreateInitialCustomerReceivableCommand(
    long ClientId,
    long InitialAmountMinor,
    DateOnly ReceivableDate,
    DateOnly? DueDate,
    string? Observation);
