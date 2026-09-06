namespace FinancialManagement.Application.Clients;

public sealed record ClientLookupQuery(
    string? SearchText = null,
    int Limit = 20);
