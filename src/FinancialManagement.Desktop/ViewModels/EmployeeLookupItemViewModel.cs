using FinancialManagement.Application.Employees;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class EmployeeLookupItemViewModel
{
    public EmployeeLookupItemViewModel(EmployeeLookupItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        UserId = item.UserId;
        DisplayName = item.DisplayName;
        Username = item.Username;
        DisplayLabel = $"{item.DisplayName} — {item.Username}";
    }

    public long UserId { get; }
    public string DisplayName { get; }
    public string Username { get; }
    public string DisplayLabel { get; }
}
