using FinancialManagement.Application.Alerts;
using FinancialManagement.Desktop.Formatting;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class DueAlertItemViewModel
{
    public DueAlertItemViewModel(FinancialDueAlert alert)
    {
        ArgumentNullException.ThrowIfNull(alert);

        Reference = alert.Reference;
        PartyName = alert.PartyName;
        DueDate = alert.DueDate.ToString("dd/MM/yyyy");
        OutstandingAmount = MoneyFormatter.FormatMinor(alert.OutstandingMinor);
        Status = alert.Status;
        StatusLabel = FormatStatus(alert.Status);
        DaysLabel = FormatDaysUntilDue(alert.DaysUntilDue);
        SourceTypeLabel = FormatSourceType(alert.SourceType);
    }

    public string Reference { get; }

    public string PartyName { get; }

    public string DueDate { get; }

    public string OutstandingAmount { get; }

    public FinancialDueAlertStatus Status { get; }

    public string StatusLabel { get; }

    public string DaysLabel { get; }

    public string SourceTypeLabel { get; }

    private static string FormatStatus(FinancialDueAlertStatus status) => status switch
    {
        FinancialDueAlertStatus.Upcoming => "À venir",
        FinancialDueAlertStatus.DueToday => "Aujourd'hui",
        FinancialDueAlertStatus.Overdue => "En retard",
        _ => "Indéterminé"
    };

    private static string FormatDaysUntilDue(int daysUntilDue) => daysUntilDue switch
    {
        1 => "Demain",
        0 => "Aujourd'hui",
        > 1 => $"Dans {daysUntilDue} jours",
        -1 => "1 jour de retard",
        _ => $"{-(long)daysUntilDue} jours de retard"
    };

    private static string FormatSourceType(FinancialDueAlertSourceType sourceType) => sourceType switch
    {
        FinancialDueAlertSourceType.Sale => "Vente",
        FinancialDueAlertSourceType.InitialCustomerReceivable => "Créance initiale",
        FinancialDueAlertSourceType.Purchase => "Achat",
        FinancialDueAlertSourceType.InitialSupplierPayable => "Dette initiale",
        _ => "Origine indéterminée"
    };
}
