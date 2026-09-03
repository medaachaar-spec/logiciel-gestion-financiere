using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Domain.Entities;

public sealed class TreasuryTransfer
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateOnly TransferDate { get; set; }
    public TreasuryAccount FromAccount { get; set; }
    public TreasuryAccount ToAccount { get; set; }
    public long AmountMinor { get; set; }
    public string? Observation { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
