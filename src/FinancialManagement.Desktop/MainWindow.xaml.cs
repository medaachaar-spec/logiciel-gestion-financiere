using System.Text;
using System.Windows;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ShellViewModel _viewModel;

    public event EventHandler? LogoutRequested;

    public MainWindow(ShellViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.LogoutRequested += ViewModel_LogoutRequested;
    }

    public Task<bool> InitializeAsync(CancellationToken cancellationToken = default) =>
        _viewModel.InitializeAsync(cancellationToken);

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.LogoutRequested -= ViewModel_LogoutRequested;
        base.OnClosed(e);
    }

    private void ViewModel_LogoutRequested(object? sender, EventArgs e)
    {
        LogoutRequested?.Invoke(this, EventArgs.Empty);
        Close();
    }
}
