namespace FinancialManagement.Domain.Entities;

public sealed class InitialCustomerReceivable
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public long ClientId { get; set; }
    public DateOnly ReceivableDate { get; set; }
    public long InitialAmountMinor { get; set; }
    public DateOnly? DueDate { get; set; }
    public string? Observation { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<CustomerPaymentAllocation> CustomerPaymentAllocations { get; set; } = [];
}
