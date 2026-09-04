using System.ComponentModel;
using System.Windows;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        UsernameInput.Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private async void SignInButton_OnClick(object sender, RoutedEventArgs e)
    {
        bool authenticated;

        try
        {
            authenticated = await _viewModel.AuthenticateAsync(PasswordInput.Password);
        }
        finally
        {
            PasswordInput.Clear();
        }

        if (authenticated)
        {
            DialogResult = true;
            return;
        }

        PasswordInput.Focus();
    }
}
