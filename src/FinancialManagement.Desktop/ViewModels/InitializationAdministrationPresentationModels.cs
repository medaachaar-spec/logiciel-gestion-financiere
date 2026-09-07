using FinancialManagement.Application.Administration;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class AdministrationUserItemViewModel(UserAdministrationItem item)
{
    public long Id { get; } = item.Id;

    public string FullName { get; } = item.FullName;

    public string Username { get; } = item.Username;

    public UserRole Role { get; } = item.Role;

    public string RoleLabel => Role == UserRole.Responsable ? "Responsable" : "Employé";

    public bool IsActive { get; } = item.IsActive;

    public string StatusLabel => IsActive ? "Actif" : "Désactivé";

    public string PermissionSummary => Role == UserRole.Responsable
        ? "Accès Responsable"
        : BuildPermissionSummary(item.Permissions);

    public IReadOnlyList<EmployeePermissionInput> Permissions { get; } = item.Permissions;

    private static string BuildPermissionSummary(IReadOnlyList<EmployeePermissionInput> permissions)
    {
        var visibleCount = permissions.Count(permission => permission.CanView);
        return visibleCount == 0 ? "Aucun module" : $"{visibleCount} module(s) visible(s)";
    }
}

public sealed class EmployeePermissionRowViewModel : ViewModelBase
{
    private bool _canView;
    private bool _canCreate;
    private bool _canModify;

    public EmployeePermissionRowViewModel(PermissionModule module)
    {
        Module = module;
        ModuleLabel = module switch
        {
            PermissionModule.Sales => "Ventes",
            PermissionModule.Purchases => "Achats",
            PermissionModule.Expenses => "Dépenses",
            PermissionModule.Clients => "Clients",
            PermissionModule.Suppliers => "Fournisseurs",
            PermissionModule.CustomerPayments => "Paiements clients",
            PermissionModule.SupplierPayments => "Paiements fournisseurs",
            _ => "Module"
        };
    }

    public PermissionModule Module { get; }

    public string ModuleLabel { get; }

    public bool CanView
    {
        get => _canView;
        set
        {
            if (!SetProperty(ref _canView, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CanUseDependentPermissions));
            if (!value)
            {
                CanCreate = false;
                CanModify = false;
            }
        }
    }

    public bool CanCreate
    {
        get => _canCreate;
        set => SetProperty(ref _canCreate, CanView && value);
    }

    public bool CanModify
    {
        get => _canModify;
        set => SetProperty(ref _canModify, CanView && value);
    }

    public bool CanUseDependentPermissions => CanView;

    public void Load(EmployeePermissionInput? permission)
    {
        CanView = permission?.CanView == true;
        CanCreate = permission?.CanCreate == true;
        CanModify = permission?.CanModify == true;
    }

    public EmployeePermissionInput ToInput() => new(Module, CanView, CanCreate, CanModify);
}

public sealed class InitialObligationListItemViewModel(
    long id,
    string reference,
    string partyName,
    DateOnly businessDate,
    long initialAmountMinor,
    long outstandingAmountMinor,
    DateOnly? dueDate,
    string? observation)
{
    public long Id { get; } = id;

    public string Reference { get; } = reference;

    public string PartyName { get; } = partyName;

    public string BusinessDate { get; } = businessDate.ToString("dd/MM/yyyy");

    public string InitialAmount { get; } = MoneyFormatter.FormatMinor(initialAmountMinor);

    public string OutstandingAmount { get; } = MoneyFormatter.FormatMinor(outstandingAmountMinor);

    public string DueDate { get; } = dueDate?.ToString("dd/MM/yyyy") ?? "—";

    public string Observation { get; } = string.IsNullOrWhiteSpace(observation) ? "—" : observation;
}

public sealed class InitialObligationPartyItemViewModel(long id, string displayName, string? identifier)
{
    public long Id { get; } = id;

    public string DisplayName { get; } = displayName;

    public string Identifier { get; } = string.IsNullOrWhiteSpace(identifier) ? "Sans identifiant" : identifier;

    public string DisplayLabel => $"{DisplayName} — {Identifier}";
}

public sealed record InitialObligationPageResponse(
    bool Success,
    bool AuthenticationFailure,
    string? ErrorMessage,
    IReadOnlyList<InitialObligationListItemViewModel> Items,
    int PageNumber,
    int TotalPages,
    int TotalCount);

public sealed record InitialObligationPartyLookupResponse(
    bool Success,
    bool AuthenticationFailure,
    string? ErrorMessage,
    IReadOnlyList<InitialObligationPartyItemViewModel> Items);

public sealed record InitialObligationCreateResponse(
    bool Success,
    bool AuthenticationFailure,
    string? ErrorMessage,
    string? Reference);
