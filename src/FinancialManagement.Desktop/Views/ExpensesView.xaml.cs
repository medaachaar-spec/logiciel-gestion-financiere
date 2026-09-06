using System.Windows;
using System.Windows.Controls;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop.Views;

public partial class ExpensesView : UserControl
{
    public ExpensesView()
    {
        InitializeComponent();
    }

    private async void ExpensesView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ExpensesViewModel viewModel)
        {
            await viewModel.LoadAsync();
        }
    }
}
