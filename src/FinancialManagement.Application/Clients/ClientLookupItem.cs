namespace FinancialManagement.Application.Clients;

public sealed record ClientLookupItem(
    long ClientId,
    string DisplayName,
    string? Identifier);
