using FinancialManagement.Application.Clients;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class ClientLookupItemViewModel
{
    public ClientLookupItemViewModel(ClientLookupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);

        ClientId = item.ClientId;
        DisplayName = item.DisplayName;
        Identifier = item.Identifier;
        DisplayLabel = string.IsNullOrWhiteSpace(item.Identifier)
            ? item.DisplayName
            : $"{item.DisplayName} — {item.Identifier}";
    }

    public long ClientId { get; }

    public string DisplayName { get; }

    public string? Identifier { get; }

    public string DisplayLabel { get; }
}
