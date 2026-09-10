using FinancialManagement.Application.Dashboard;
using FinancialManagement.Desktop.Formatting;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class DashboardPartyBalanceViewModel
{
    public DashboardPartyBalanceViewModel(FinancialDashboardPartyBalance balance)
    {
        ArgumentNullException.ThrowIfNull(balance);
        PartyName = balance.PartyName;
        OutstandingAmount = MoneyFormatter.FormatMinor(balance.OutstandingMinor);
        AccessibilityName = $"{PartyName}, encours {OutstandingAmount}";
    }

    public string PartyName { get; }

    public string OutstandingAmount { get; }

    public string AccessibilityName { get; }
}
