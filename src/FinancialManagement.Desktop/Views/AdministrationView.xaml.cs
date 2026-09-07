using System.Windows;
using System.Windows.Controls;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop.Views;

public partial class AdministrationView : UserControl
{
    public AdministrationView()
    {
        InitializeComponent();
    }

    private async void AdministrationView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdministrationViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }

    private void AdministrationView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is AdministrationViewModel viewModel)
        {
            viewModel.Deactivate();
        }

        ClearPasswords();
    }

    private async void CreateEmployeeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AdministrationViewModel viewModel)
        {
            return;
        }

        var credentials = new CredentialSubmission(
            EmployeePasswordInput.Password,
            EmployeePasswordConfirmationInput.Password);
        try
        {
            await viewModel.CreateEmployeeCommand.ExecuteAsync(credentials);
        }
        finally
        {
            EmployeePasswordInput.Clear();
            EmployeePasswordConfirmationInput.Clear();
        }
    }

    private async void ResetPasswordButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not AdministrationViewModel viewModel)
        {
            return;
        }

        var credentials = new CredentialSubmission(
            ResetPasswordInput.Password,
            ResetPasswordConfirmationInput.Password);
        try
        {
            await viewModel.ResetPasswordCommand.ExecuteAsync(credentials);
        }
        finally
        {
            ResetPasswordInput.Clear();
            ResetPasswordConfirmationInput.Clear();
        }
    }

    private void ClearPasswords()
    {
        EmployeePasswordInput.Clear();
        EmployeePasswordConfirmationInput.Clear();
        ResetPasswordInput.Clear();
        ResetPasswordConfirmationInput.Clear();
    }
}
