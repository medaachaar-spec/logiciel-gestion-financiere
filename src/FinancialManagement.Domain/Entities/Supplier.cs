namespace FinancialManagement.Domain.Entities;

public sealed class Supplier
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

    public ICollection<Purchase> Purchases { get; set; } = [];
    public ICollection<SupplierPayment> SupplierPayments { get; set; } = [];
    public ICollection<InitialSupplierPayable> InitialSupplierPayables { get; set; } = [];
}
