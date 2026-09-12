using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop;

public partial class LoginWindow : Window
{
    private readonly LoginViewModel _viewModel;

    public LoginWindow(LoginViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = _viewModel;
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        UsernameInput.Focus();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel?.IsBusy == true)
        {
            e.Cancel = true;
            return;
        }

        ClearPasswordInputs();
        base.OnClosing(e);
    }

    private async void SignInButton_OnClick(object sender, RoutedEventArgs e)
    {
        var authenticated = await AuthenticateCurrentPasswordAsync();

        if (authenticated)
        {
            DialogResult = true;
            return;
        }

        PasswordInput.Focus();
    }

    internal async Task<bool> AuthenticateCurrentPasswordAsync(
        CancellationToken cancellationToken = default)
    {
        var password = VisiblePasswordInput.Visibility == Visibility.Visible
            ? VisiblePasswordInput.Text
            : PasswordInput.Password;

        try
        {
            return await _viewModel.AuthenticateAsync(password, cancellationToken);
        }
        finally
        {
            ClearPasswordInputs();
        }
    }

    private void PasswordVisibilityToggle_OnChecked(object sender, RoutedEventArgs e) =>
        SetPasswordVisibility(isVisible: true);

    private void PasswordVisibilityToggle_OnUnchecked(object sender, RoutedEventArgs e) =>
        SetPasswordVisibility(isVisible: false);

    private void SetPasswordVisibility(bool isVisible)
    {
        if (isVisible)
        {
            VisiblePasswordInput.Text = PasswordInput.Password;
            PasswordInput.Visibility = Visibility.Collapsed;
            VisiblePasswordInput.Visibility = Visibility.Visible;
            UpdatePasswordToggleAccessibility(isVisible: true);
            VisiblePasswordInput.Focus();
            VisiblePasswordInput.CaretIndex = VisiblePasswordInput.Text.Length;
            return;
        }

        if (VisiblePasswordInput.Visibility == Visibility.Visible)
        {
            PasswordInput.Password = VisiblePasswordInput.Text;
            VisiblePasswordInput.Clear();
        }

        VisiblePasswordInput.Visibility = Visibility.Collapsed;
        PasswordInput.Visibility = Visibility.Visible;
        UpdatePasswordToggleAccessibility(isVisible: false);
        PasswordInput.Focus();
    }

    private void ClearPasswordInputs()
    {
        VisiblePasswordInput.Clear();
        PasswordInput.Clear();
        PasswordVisibilityToggle.IsChecked = false;
        VisiblePasswordInput.Visibility = Visibility.Collapsed;
        PasswordInput.Visibility = Visibility.Visible;
        UpdatePasswordToggleAccessibility(isVisible: false);
    }

    private void UpdatePasswordToggleAccessibility(bool isVisible)
    {
        var accessibleName = isVisible
            ? "Masquer le mot de passe"
            : "Afficher le mot de passe";

        AutomationProperties.SetName(PasswordVisibilityToggle, accessibleName);
        PasswordVisibilityToggle.ToolTip = accessibleName;
    }
}
