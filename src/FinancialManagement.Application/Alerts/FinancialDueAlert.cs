namespace FinancialManagement.Application.Alerts;

public sealed record FinancialDueAlert(
    FinancialDueAlertSourceType SourceType,
    FinancialDueAlertSide Side,
    long SourceId,
    string Reference,
    long PartyId,
    string PartyName,
    DateOnly DueDate,
    long OutstandingMinor,
    int DaysUntilDue,
    FinancialDueAlertStatus Status,
    long OriginalAmountMinor);
