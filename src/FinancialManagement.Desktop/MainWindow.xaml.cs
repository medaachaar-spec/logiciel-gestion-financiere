using System.Text;
using System.Windows;
using FinancialManagement.Desktop.ViewModels;

namespace FinancialManagement.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow(ShellViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
