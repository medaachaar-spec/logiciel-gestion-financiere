using System.Windows;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Desktop.Services;

namespace FinancialManagement.Desktop.Views;

public partial class CustomerPaymentReceiptWindow : Window
{
    private readonly CustomerPaymentReceiptItem receipt;
    private readonly ICustomerPaymentReceiptPrinter printer;

    public CustomerPaymentReceiptWindow(
        CustomerPaymentReceiptItem receipt,
        ICustomerPaymentReceiptPrinter printer)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(printer);
        this.receipt = receipt;
        this.printer = printer;
        InitializeComponent();
        ReceiptPreview.Document = CustomerPaymentReceiptDocumentFactory.Create(receipt);
    }

    private void PrintOnClick(object sender, RoutedEventArgs e)
    {
        var result = printer.Print(receipt);
        PrintStatus.Text = result.Message;
    }

    private void CloseOnClick(object sender, RoutedEventArgs e) => Close();
}
