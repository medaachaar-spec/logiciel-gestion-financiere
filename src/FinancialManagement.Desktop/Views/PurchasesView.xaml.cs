using System.Windows;
using System.Windows.Controls;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop.Views;

public partial class PurchasesView : UserControl
{
    public PurchasesView()
    {
        InitializeComponent();
    }

    private async void PurchasesView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PurchasesViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }
}
