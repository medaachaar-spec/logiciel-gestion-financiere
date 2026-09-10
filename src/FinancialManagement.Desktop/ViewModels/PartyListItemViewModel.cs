using FinancialManagement.Desktop.Formatting;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class PartyListItemViewModel
{
    public PartyListItemViewModel(
        long id,
        string name,
        string? phone,
        string? company,
        string? identifier,
        long currentBalanceMinorUnits,
        string partyKind,
        string currentBalanceLabel)
    {
        Id = id;
        Name = name;
        PhoneDisplay = DisplayOptional(phone);
        CompanyDisplay = DisplayOptional(company);
        IdentifierDisplay = DisplayOptional(identifier);
        CurrentBalanceMinorUnits = currentBalanceMinorUnits;
        CurrentBalanceDisplay = MoneyFormatter.FormatMinor(currentBalanceMinorUnits);
        AccessibleDescription =
            $"{partyKind} {name}, téléphone {PhoneDisplay}, société {CompanyDisplay}, " +
            $"{currentBalanceLabel} {CurrentBalanceDisplay}";
    }

    public long Id { get; }

    public string Name { get; }

    public string PhoneDisplay { get; }

    public string CompanyDisplay { get; }

    public string IdentifierDisplay { get; }

    public long CurrentBalanceMinorUnits { get; }

    public string CurrentBalanceDisplay { get; }

    public string AccessibleDescription { get; }

    private static string DisplayOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
