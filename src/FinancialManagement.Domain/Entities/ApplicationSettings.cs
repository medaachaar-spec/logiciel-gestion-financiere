namespace FinancialManagement.Domain.Entities;

public sealed class ApplicationSettings
{
    public long Id { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
