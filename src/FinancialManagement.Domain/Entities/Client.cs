namespace FinancialManagement.Domain.Entities;

public sealed class Client
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? Address { get; set; }
    public string? Company { get; set; }
    public string? Identifier { get; set; }
    public string? Observation { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<Sale> Sales { get; set; } = [];
    public ICollection<CustomerPayment> CustomerPayments { get; set; } = [];
    public ICollection<InitialCustomerReceivable> InitialCustomerReceivables { get; set; } = [];
}
