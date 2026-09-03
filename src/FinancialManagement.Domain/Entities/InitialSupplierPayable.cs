namespace FinancialManagement.Domain.Entities;

public sealed class InitialSupplierPayable
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public DateOnly PayableDate { get; set; }
    public long InitialAmountMinor { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Observation { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<SupplierPaymentAllocation> SupplierPaymentAllocations { get; set; } = [];
}
