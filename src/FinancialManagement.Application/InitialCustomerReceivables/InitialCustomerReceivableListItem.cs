namespace FinancialManagement.Application.InitialCustomerReceivables;

public sealed record InitialCustomerReceivableListItem(
    long Id, string Reference, long ClientId, string ClientName, DateOnly ReceivableDate,
    long InitialAmountMinor, long OutstandingAmountMinor, DateOnly? DueDate, string? Observation);
