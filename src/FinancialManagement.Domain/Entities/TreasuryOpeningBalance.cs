namespace FinancialManagement.Domain.Entities;

public sealed class TreasuryOpeningBalance
{
    public long Id { get; set; }
    public DateOnly OpeningDate { get; set; }
    public long CashAmountMinor { get; set; }
    public long BankAmountMinor { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
