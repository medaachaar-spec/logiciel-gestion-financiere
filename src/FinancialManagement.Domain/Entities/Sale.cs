namespace FinancialManagement.Domain.Entities;

public sealed class Sale
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateOnly SaleDate { get; set; }
    public long TotalAmountMinor { get; set; }
    public long? ClientId { get; set; }
    public string? Observation { get; set; }
    public DateOnly? DueDate { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<CustomerPaymentAllocation> CustomerPaymentAllocations { get; set; } = [];
}
