using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.Treasury;

public sealed record CreateTreasuryTransferCommand(
    TreasuryAccount FromAccount,
    TreasuryAccount ToAccount,
    long AmountMinor,
    string? Observation);
