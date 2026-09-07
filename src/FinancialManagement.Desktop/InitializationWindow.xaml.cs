using System.Windows;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop;

public partial class InitializationWindow : Window
{
    private readonly InitializationViewModel _viewModel;

    public InitializationWindow(InitializationViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.InitializationCompleted += ViewModel_InitializationCompleted;
        _viewModel.AuthenticationInvalidated += ViewModel_AuthenticationInvalidated;
    }

    public async Task<bool> PrepareAsync(CancellationToken cancellationToken = default)
    {
        await _viewModel.LoadAsync(cancellationToken);
        return _viewModel.IsInitializationComplete;
    }

    public Task StopAsync() => _viewModel.StopAsync();

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        OpeningDateInput.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.InitializationCompleted -= ViewModel_InitializationCompleted;
        _viewModel.AuthenticationInvalidated -= ViewModel_AuthenticationInvalidated;
        _viewModel.Stop();
        base.OnClosed(e);
    }

    private void ViewModel_InitializationCompleted(object? sender, EventArgs e) => DialogResult = true;

    private void ViewModel_AuthenticationInvalidated(object? sender, EventArgs e)
    {
        if (IsVisible)
        {
            DialogResult = false;
        }
    }
}
