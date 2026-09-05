using System.Windows.Input;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class NavigationItemViewModel : ViewModelBase
{
    private readonly Lazy<ViewModelBase> _page;
    private bool _isVisible;
    private bool _isSelected;

    public NavigationItemViewModel(
        string title,
        string iconGlyph,
        string pageKey,
        Func<ViewModelBase> pageFactory,
        ICommand command,
        PermissionModule? requiredModule = null,
        ResponsibleCapability? requiredCapability = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(pageKey);
        ArgumentNullException.ThrowIfNull(pageFactory);
        ArgumentNullException.ThrowIfNull(command);

        if (requiredModule.HasValue == requiredCapability.HasValue)
        {
            throw new ArgumentException(
                "A navigation route must define exactly one authorization requirement.");
        }

        Title = title;
        IconGlyph = iconGlyph;
        PageKey = pageKey;
        _page = new Lazy<ViewModelBase>(pageFactory);
        Command = command;
        RequiredModule = requiredModule;
        RequiredAction = requiredModule.HasValue ? PermissionAction.View : null;
        RequiredCapability = requiredCapability;
    }

    public string Title { get; }

    public string IconGlyph { get; }

    public string PageKey { get; }

    public ICommand Command { get; }

    public PermissionModule? RequiredModule { get; }

    public PermissionAction? RequiredAction { get; }

    public ResponsibleCapability? RequiredCapability { get; }

    public bool IsVisible => _isVisible;

    public bool IsSelected => _isSelected;

    internal ViewModelBase ResolvePage() =>
        _page.Value
        ?? throw new InvalidOperationException("A navigation page factory returned null.");

    internal void SetVisibility(bool isVisible) =>
        SetProperty(ref _isVisible, isVisible, nameof(IsVisible));

    internal void SetSelected(bool isSelected) =>
        SetProperty(ref _isSelected, isSelected, nameof(IsSelected));
}
