using System.Windows;
using System.Windows.Controls;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop.Views;

public partial class InitialObligationView : UserControl
{
    public InitialObligationView()
    {
        InitializeComponent();
    }

    private async void InitialObligationView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is InitialObligationViewModelBase viewModel)
        {
            await viewModel.LoadAsync();
        }
    }

    private void InitialObligationView_OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is InitialObligationViewModelBase viewModel)
        {
            viewModel.Deactivate();
        }
    }
}
