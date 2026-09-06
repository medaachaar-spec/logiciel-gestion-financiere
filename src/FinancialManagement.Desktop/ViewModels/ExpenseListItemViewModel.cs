using FinancialManagement.Application.Expenses;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class ExpenseListItemViewModel
{
    public ExpenseListItemViewModel(ExpenseListItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        Id = item.Id;
        Reference = item.Reference;
        ExpenseDateDisplay = item.ExpenseDate.ToString("dd/MM/yyyy");
        ExpenseCategoryName = item.ExpenseCategoryName;
        AmountDisplay = MoneyFormatter.FormatMinor(item.AmountMinor);
        PaymentMethodDisplay = item.PaymentMethod switch
        {
            PaymentMethod.Cash => "Espèces",
            PaymentMethod.BankTransfer => "Virement bancaire",
            _ => "Non disponible"
        };
        ConcernedEmployeeDisplay = string.IsNullOrWhiteSpace(item.ConcernedEmployeeDisplayName)
            ? "—"
            : item.ConcernedEmployeeDisplayName;
        ObservationDisplay = string.IsNullOrWhiteSpace(item.Observation) ? "—" : item.Observation;
        AccessibilityName =
            $"Dépense {Reference}, date {ExpenseDateDisplay}, catégorie {ExpenseCategoryName}, montant {AmountDisplay}, paiement {PaymentMethodDisplay}, employé {ConcernedEmployeeDisplay}";
    }

    public long Id { get; }
    public string Reference { get; }
    public string ExpenseDateDisplay { get; }
    public string ExpenseCategoryName { get; }
    public string AmountDisplay { get; }
    public string PaymentMethodDisplay { get; }
    public string ConcernedEmployeeDisplay { get; }
    public string ObservationDisplay { get; }
    public string AccessibilityName { get; }
}
