using System.Windows;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop;

public partial class FirstRunWindow : Window
{
    private readonly BootstrapViewModel _viewModel;

    public FirstRunWindow(BootstrapViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.ReadyForLogin += ViewModel_ReadyForLogin;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        FullNameInput.Focus();
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.ReadyForLogin -= ViewModel_ReadyForLogin;
        _viewModel.Stop();
        PasswordInput.Clear();
        ConfirmationInput.Clear();
        base.OnClosed(e);
    }

    public Task StopAsync() => _viewModel.StopAsync();

    private async void SubmitButton_OnClick(object sender, RoutedEventArgs e)
    {
        var credentials = new CredentialSubmission(PasswordInput.Password, ConfirmationInput.Password);
        try
        {
            await ((AsyncRelayCommand)_viewModel.SubmitCommand).ExecuteAsync(credentials);
        }
        finally
        {
            PasswordInput.Clear();
            ConfirmationInput.Clear();
        }
    }

    private void ViewModel_ReadyForLogin(object? sender, EventArgs e)
    {
        DialogResult = true;
    }
}
