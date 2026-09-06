using FinancialManagement.Application.Suppliers;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class SupplierLookupItemViewModel
{
    public SupplierLookupItemViewModel(SupplierLookupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        SupplierId = item.SupplierId;
        DisplayName = item.DisplayName;
        Identifier = item.Identifier;
        DisplayLabel = string.IsNullOrWhiteSpace(item.Identifier)
            ? item.DisplayName
            : $"{item.DisplayName} — {item.Identifier}";
    }

    public long SupplierId { get; }

    public string DisplayName { get; }

    public string? Identifier { get; }

    public string DisplayLabel { get; }
}
