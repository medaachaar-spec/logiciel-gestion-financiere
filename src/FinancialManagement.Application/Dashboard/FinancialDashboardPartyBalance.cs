namespace FinancialManagement.Application.Dashboard;

public sealed record FinancialDashboardPartyBalance(
    long PartyId,
    string PartyName,
    long OutstandingMinor);
