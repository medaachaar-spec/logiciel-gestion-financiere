namespace FinancialManagement.Domain.Entities;

public sealed class SupplierPaymentAllocation
{
    public long Id { get; set; }
    public long SupplierPaymentId { get; set; }
    public long? PurchaseId { get; set; }
    public long? InitialSupplierPayableId { get; set; }
    public long AmountMinor { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
