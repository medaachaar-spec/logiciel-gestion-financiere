using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Domain.Entities;

public sealed class SupplierPayment
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public long SupplierId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public long AmountMinor { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? Observation { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<SupplierPaymentAllocation> Allocations { get; set; } = [];
}
