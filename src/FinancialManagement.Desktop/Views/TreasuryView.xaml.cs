using System.Windows;
using System.Windows.Controls;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop.Views;

public partial class TreasuryView : UserControl
{
    public TreasuryView()
    {
        InitializeComponent();
    }

    private async void TreasuryView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is TreasuryViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }
}
