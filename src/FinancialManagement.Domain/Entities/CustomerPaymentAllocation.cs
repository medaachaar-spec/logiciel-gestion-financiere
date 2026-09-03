namespace FinancialManagement.Domain.Entities;

public sealed class CustomerPaymentAllocation
{
    public long Id { get; set; }
    public long CustomerPaymentId { get; set; }
    public long? SaleId { get; set; }
    public long? InitialCustomerReceivableId { get; set; }
    public long AmountMinor { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
