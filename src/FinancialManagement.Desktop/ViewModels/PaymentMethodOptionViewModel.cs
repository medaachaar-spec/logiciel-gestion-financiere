using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed record PaymentMethodOptionViewModel(
    PaymentMethod Value,
    string DisplayName);
