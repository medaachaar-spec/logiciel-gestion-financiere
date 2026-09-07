namespace FinancialManagement.Application.CustomerPayments;

public sealed class CustomerPaymentObligationLookupResult
{
    private CustomerPaymentObligationLookupResult(
        bool success,
        CustomerPaymentObligationLookupFailureReason failureReason,
        IReadOnlyList<CustomerPaymentObligationLookupItem> items,
        long clientId,
        int limit)
    {
        Success = success;
        FailureReason = failureReason;
        Items = items;
        ClientId = clientId;
        Limit = limit;
    }

    public bool Success { get; }

    public CustomerPaymentObligationLookupFailureReason FailureReason { get; }

    public IReadOnlyList<CustomerPaymentObligationLookupItem> Items { get; }

    public long ClientId { get; }

    public int Limit { get; }

    public static CustomerPaymentObligationLookupResult Failed(
        CustomerPaymentObligationLookupQuery query,
        CustomerPaymentObligationLookupFailureReason failureReason) =>
        new(false, failureReason, Array.Empty<CustomerPaymentObligationLookupItem>(), query.ClientId, query.Limit);

    public static CustomerPaymentObligationLookupResult Succeeded(
        CustomerPaymentObligationLookupQuery query,
        IReadOnlyList<CustomerPaymentObligationLookupItem> items) =>
        new(true, CustomerPaymentObligationLookupFailureReason.None, items, query.ClientId, query.Limit);
}
