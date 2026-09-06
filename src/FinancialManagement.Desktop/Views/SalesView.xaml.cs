using System.Windows;
using System.Windows.Controls;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop.Views;

public partial class SalesView : UserControl
{
    public SalesView()
    {
        InitializeComponent();
    }

    private async void SalesView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is SalesViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }
}
