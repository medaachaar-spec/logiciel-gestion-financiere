using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Domain.Entities;

public sealed class CustomerPayment
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public long ClientId { get; set; }
    public DateOnly PaymentDate { get; set; }
    public long AmountMinor { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string? Observation { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<CustomerPaymentAllocation> Allocations { get; set; } = [];
}
