using FinancialManagement.Application.Purchases;
using FinancialManagement.Desktop.Formatting;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class PurchaseListItemViewModel
{
    public PurchaseListItemViewModel(PurchaseListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Id = item.Id;
        Reference = item.Reference;
        PurchaseDate = item.PurchaseDate.ToString("dd/MM/yyyy");
        Supplier = item.SupplierName;
        Amount = MoneyFormatter.FormatMinor(item.TotalAmountMinor);
        DueDate = item.DueDate?.ToString("dd/MM/yyyy") ?? "—";
        Observation = string.IsNullOrWhiteSpace(item.Observation) ? "—" : item.Observation;
        AccessibilityName =
            $"Achat {Reference}, date {PurchaseDate}, fournisseur {Supplier}, montant {Amount}, échéance {DueDate}";
    }

    public long Id { get; }
    public string Reference { get; }
    public string PurchaseDate { get; }
    public string Supplier { get; }
    public string Amount { get; }
    public string DueDate { get; }
    public string Observation { get; }
    public string AccessibilityName { get; }
}
