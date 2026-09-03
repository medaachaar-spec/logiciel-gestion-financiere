namespace FinancialManagement.Domain.Entities;

public sealed class Purchase
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateOnly PurchaseDate { get; set; }
    public long TotalAmountMinor { get; set; }
    public long SupplierId { get; set; }
    public string? Observation { get; set; }
    public DateOnly? DueDate { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<SupplierPaymentAllocation> SupplierPaymentAllocations { get; set; } = [];
}
