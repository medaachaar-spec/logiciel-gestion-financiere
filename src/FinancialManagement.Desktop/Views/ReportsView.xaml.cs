using System.Windows;
using System.Windows.Controls;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop.Views;

public partial class ReportsView : UserControl
{
    public ReportsView() => InitializeComponent();

    private async void ReportsView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ReportsViewModel viewModel)
            await viewModel.LoadAsync();
    }
}
