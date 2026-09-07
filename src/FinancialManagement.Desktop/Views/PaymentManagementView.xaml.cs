using System.Windows;
using System.Windows.Controls;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop.Views;

public partial class PaymentManagementView : UserControl
{
    public PaymentManagementView()
    {
        InitializeComponent();
    }

    private async void PaymentManagementView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PaymentManagementViewModelBase viewModel)
        {
            await viewModel.LoadAsync();
        }
    }

    private void PaymentManagementView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PaymentManagementViewModelBase viewModel)
        {
            viewModel.Deactivate();
        }
    }
}
