namespace FinancialManagement.Application.CustomerPayments;

public sealed record CustomerPaymentObligationLookupQuery(
    long ClientId,
    CustomerPaymentObligationLookupPurpose Purpose,
    string? SearchText = null,
    int Limit = 50);
