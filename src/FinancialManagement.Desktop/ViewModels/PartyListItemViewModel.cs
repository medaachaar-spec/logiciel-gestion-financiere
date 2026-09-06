namespace FinancialManagement.Desktop.ViewModels;

public sealed class PartyListItemViewModel
{
    public PartyListItemViewModel(
        long id,
        string name,
        string? phone,
        string? company,
        string? identifier,
        string partyKind)
    {
        Id = id;
        Name = name;
        PhoneDisplay = DisplayOptional(phone);
        CompanyDisplay = DisplayOptional(company);
        IdentifierDisplay = DisplayOptional(identifier);
        AccessibleDescription =
            $"{partyKind} {name}, téléphone {PhoneDisplay}, société {CompanyDisplay}, identifiant {IdentifierDisplay}";
    }

    public long Id { get; }

    public string Name { get; }

    public string PhoneDisplay { get; }

    public string CompanyDisplay { get; }

    public string IdentifierDisplay { get; }

    public string AccessibleDescription { get; }

    private static string DisplayOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "—" : value;
}
