using FinancialManagement.Application.Sales;
using FinancialManagement.Desktop.Formatting;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class SaleListItemViewModel
{
    public SaleListItemViewModel(SaleListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Id = item.Id;
        Reference = item.Reference;
        SaleDate = item.SaleDate.ToString("dd/MM/yyyy");
        Client = string.IsNullOrWhiteSpace(item.ClientName)
            ? "Client anonyme"
            : item.ClientName;
        Amount = MoneyFormatter.FormatMinor(item.TotalAmountMinor);
        DueDate = item.DueDate?.ToString("dd/MM/yyyy") ?? "—";
        Observation = string.IsNullOrWhiteSpace(item.Observation)
            ? "—"
            : item.Observation;
        AccessibilityName =
            $"Vente {Reference}, date {SaleDate}, {Client}, montant {Amount}, échéance {DueDate}";
    }

    public long Id { get; }

    public string Reference { get; }

    public string SaleDate { get; }

    public string Client { get; }

    public string Amount { get; }

    public string DueDate { get; }

    public string Observation { get; }

    public string AccessibilityName { get; }
}
