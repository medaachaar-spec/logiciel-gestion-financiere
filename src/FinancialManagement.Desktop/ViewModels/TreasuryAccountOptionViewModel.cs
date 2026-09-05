using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed record TreasuryAccountOptionViewModel(
    TreasuryAccount Account,
    string DisplayName);
