using System.Windows;
using System.Windows.Controls;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop.Views;

public partial class PartyManagementView : UserControl
{
    public PartyManagementView()
    {
        InitializeComponent();
    }

    private async void PartyManagementView_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is PartyManagementViewModelBase viewModel)
        {
            await viewModel.LoadAsync();
        }
    }
}
