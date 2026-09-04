using System.Windows.Input;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class NavigationItemViewModel : ViewModelBase
{
    private bool _isVisible = true;
    private bool _isSelected;

    public NavigationItemViewModel(
        string title,
        string iconGlyph,
        string pageKey,
        PlaceholderPageViewModel page,
        ICommand command)
    {
        Title = title;
        IconGlyph = iconGlyph;
        PageKey = pageKey;
        Page = page;
        Command = command;
    }

    public string Title { get; }

    public string IconGlyph { get; }

    public string PageKey { get; }

    public PlaceholderPageViewModel Page { get; }

    public ICommand Command { get; }

    // Reserved for the future permission-aware navigation layer.
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
