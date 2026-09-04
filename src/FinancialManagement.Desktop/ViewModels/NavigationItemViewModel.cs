using System.Windows.Input;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class NavigationItemViewModel : ViewModelBase
{
    private bool _isVisible;
    private bool _isSelected;

    public NavigationItemViewModel(
        string title,
        string iconGlyph,
        string pageKey,
        PlaceholderPageViewModel page,
        ICommand command,
        PermissionModule? requiredModule = null,
        ResponsibleCapability? requiredCapability = null)
    {
        Title = title;
        IconGlyph = iconGlyph;
        PageKey = pageKey;
        Page = page;
        Command = command;
        RequiredModule = requiredModule;
        RequiredAction = requiredModule.HasValue ? PermissionAction.View : null;
        RequiredCapability = requiredCapability;
    }

    public string Title { get; }

    public string IconGlyph { get; }

    public string PageKey { get; }

    public PlaceholderPageViewModel Page { get; }

    public ICommand Command { get; }

    public PermissionModule? RequiredModule { get; }

    public PermissionAction? RequiredAction { get; }

    public ResponsibleCapability? RequiredCapability { get; }

    public bool IsVisible
    {
        get => _isVisible;
        set => SetProperty(ref _isVisible, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }
}
