namespace FinancialManagement.Application.Treasury;

public sealed record CreateTreasuryOpeningBalanceCommand(
    DateOnly OpeningDate,
    long CashAmountMinor,
    long BankAmountMinor);
