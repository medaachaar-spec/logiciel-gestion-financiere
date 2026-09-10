using FinancialManagement.Application.Dashboard;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class DashboardPeriodOption(
    FinancialPeriodPreset preset,
    string displayName) : ViewModelBase
{
    private bool _isSelected;

    public FinancialPeriodPreset Preset { get; } = preset;

    public string DisplayName { get; } = displayName;

    public bool IsSelected
    {
        get => _isSelected;
        private set => SetProperty(ref _isSelected, value);
    }

    internal void SetSelected(bool selected) => IsSelected = selected;
}
