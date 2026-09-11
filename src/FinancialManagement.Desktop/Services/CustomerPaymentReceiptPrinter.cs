using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Desktop.Views;

namespace FinancialManagement.Desktop.Services;

public interface ICustomerPaymentReceiptPrinter
{
    CustomerPaymentReceiptPrintResult Print(CustomerPaymentReceiptItem receipt);
}

public sealed record CustomerPaymentReceiptPrintResult(
    CustomerPaymentReceiptPrintOutcome Outcome,
    string Message);

public enum CustomerPaymentReceiptPrintOutcome
{
    Printed,
    Cancelled,
    Failed
}

internal interface ICustomerPaymentPrintGateway
{
    bool? ShowDialog();
    double PrintableAreaWidth { get; }
    double PrintableAreaHeight { get; }
    void PrintDocument(DocumentPaginator paginator, string description);
}

internal sealed class WpfCustomerPaymentPrintGateway : ICustomerPaymentPrintGateway
{
    private readonly PrintDialog dialog = new();

    public bool? ShowDialog() => dialog.ShowDialog();
    public double PrintableAreaWidth => dialog.PrintableAreaWidth;
    public double PrintableAreaHeight => dialog.PrintableAreaHeight;
    public void PrintDocument(DocumentPaginator paginator, string description) =>
        dialog.PrintDocument(paginator, description);
}

internal sealed class WpfCustomerPaymentReceiptPrinter : ICustomerPaymentReceiptPrinter
{
    private readonly ICustomerPaymentPrintGateway gateway;

    public WpfCustomerPaymentReceiptPrinter()
        : this(new WpfCustomerPaymentPrintGateway())
    {
    }

    internal WpfCustomerPaymentReceiptPrinter(ICustomerPaymentPrintGateway gateway)
    {
        ArgumentNullException.ThrowIfNull(gateway);
        this.gateway = gateway;
    }

    public CustomerPaymentReceiptPrintResult Print(CustomerPaymentReceiptItem receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        try
        {
            if (gateway.ShowDialog() != true)
            {
                return new CustomerPaymentReceiptPrintResult(
                    CustomerPaymentReceiptPrintOutcome.Cancelled,
                    "Impression annulée. L'encaissement reste enregistré.");
            }

            var document = CustomerPaymentReceiptDocumentFactory.Create(receipt);
            document.PageHeight = Math.Max(
                CustomerPaymentReceiptDocumentFactory.MinimumPageHeightDip,
                gateway.PrintableAreaHeight);
            document.PageWidth = Math.Min(
                CustomerPaymentReceiptDocumentFactory.PaperWidthDip,
                gateway.PrintableAreaWidth);
            document.ColumnWidth = double.PositiveInfinity;

            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.PageSize = new Size(document.PageWidth, document.PageHeight);
            gateway.PrintDocument(paginator, $"Reçu {receipt.PaymentReference}");
            return new CustomerPaymentReceiptPrintResult(
                CustomerPaymentReceiptPrintOutcome.Printed,
                "Reçu envoyé à l'imprimante sélectionnée.");
        }
        catch (Exception)
        {
            return new CustomerPaymentReceiptPrintResult(
                CustomerPaymentReceiptPrintOutcome.Failed,
                "Impression impossible. Vérifiez l'imprimante sélectionnée ; l'encaissement reste enregistré.");
        }
    }
}
