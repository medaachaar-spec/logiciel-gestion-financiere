using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Desktop.ViewModels;
using FinancialManagement.Desktop.Views;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.Tests;

public sealed class CustomerPaymentReceiptPresentationTests
{
    [Fact]
    public void Receipt_document_uses_80_mm_paper_and_72_mm_content_width()
    {
        Assert.Equal(80d, CustomerPaymentReceiptDocumentFactory.PaperWidthMillimetres);
        Assert.Equal(72d, CustomerPaymentReceiptDocumentFactory.ContentWidthMillimetres);
        Assert.Equal(
            CustomerPaymentReceiptDocumentFactory.PaperWidthDip,
            CustomerPaymentReceiptDocumentFactory.ContentWidthDip
                + 2 * CustomerPaymentReceiptDocumentFactory.MarginDip,
            precision: 8);

        WpfTestHost.Invoke(() =>
        {
            var document = CustomerPaymentReceiptDocumentFactory.Create(CreateReceipt());
            Assert.Equal(CustomerPaymentReceiptDocumentFactory.PaperWidthDip, document.PageWidth, 8);
            Assert.Equal(CustomerPaymentReceiptDocumentFactory.MarginDip, document.PagePadding.Left, 8);
            Assert.Equal(CustomerPaymentReceiptDocumentFactory.MarginDip, document.PagePadding.Right, 8);
            Assert.Equal(double.PositiveInfinity, document.ColumnWidth);
        });
    }

    [Fact]
    public void Long_business_text_is_preserved_in_flowing_document_without_fixed_wide_table()
    {
        WpfTestHost.Invoke(() =>
        {
            var longReference = "VTE-REFERENCE-TRES-LONGUE-QUI-DOIT-SE-REPLIER-SANS-ETRE-TRONQUEE-001";
            var receipt = CreateReceipt(longReference);
            var document = CustomerPaymentReceiptDocumentFactory.Create(receipt);
            var text = new TextRange(document.ContentStart, document.ContentEnd).Text;

            Assert.Contains(longReference, text);
            Assert.Empty(document.Blocks.OfType<Table>());
            Assert.Contains(MoneyRows(document), row =>
                MoneyLabel(row).Text == "Avance non affectée");
            Assert.Contains(document.Blocks.OfType<Paragraph>(), paragraph =>
                new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim() == "N°");
            Assert.Contains(document.Blocks.OfType<Paragraph>(), paragraph =>
                new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text.Trim() == longReference);
            Assert.All(MoneyRows(document), row =>
                Assert.True(row.Width <= document.PageWidth
                    - document.PagePadding.Left
                    - document.PagePadding.Right));
        });
    }

    [Fact]
    public void Typical_money_rows_keep_a_real_label_column_and_a_visible_right_aligned_amount()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = new CustomerPaymentReceiptWindow(CreateReceipt(), new StubPrinter());
            window.Show();
            window.UpdateLayout();
            var preview = Assert.IsType<FlowDocumentScrollViewer>(window.FindName("ReceiptPreview"));
            var rows = MoneyRows(preview.Document).ToArray();

            Assert.Equal(6, rows.Length);
            Assert.Contains(rows, row => MoneyLabel(row).Text == "Montant vente"
                && MoneyAmount(row).Text == MoneyFormatter.FormatMinor(430_000));
            Assert.Contains(rows, row => MoneyLabel(row).Text == "Avance non affectée"
                && MoneyAmount(row).Text == MoneyFormatter.FormatMinor(30_000));
            Assert.All(rows, row =>
            {
                Assert.Equal(CustomerPaymentReceiptDocumentFactory.ContentWidthDip, row.ActualWidth, 3);
                Assert.True(row.ColumnDefinitions[0].ActualWidth >=
                    CustomerPaymentReceiptDocumentFactory.MinimumMoneyLabelWidthDip);
                Assert.True(row.ColumnDefinitions[1].ActualWidth > 0);
                Assert.True(row.ColumnDefinitions.Sum(column => column.ActualWidth) <= row.ActualWidth + 0.1);
                var label = MoneyLabel(row);
                var amount = MoneyAmount(row);
                Assert.Equal(TextWrapping.Wrap, label.TextWrapping);
                Assert.True(label.ActualHeight < 30, $"'{label.Text}' wrapped unexpectedly.");
                Assert.Equal(TextWrapping.NoWrap, amount.TextWrapping);
                Assert.True(amount.ActualWidth > 25, $"'{amount.Text}' is not visibly measured.");
                Assert.Equal(TextAlignment.Right, amount.TextAlignment);
                Assert.Equal(HorizontalAlignment.Right, amount.HorizontalAlignment);
                var amountOrigin = amount.TranslatePoint(new Point(0, 0), row);
                Assert.True(amountOrigin.X >= CustomerPaymentReceiptDocumentFactory.MinimumMoneyLabelWidthDip);
                Assert.True(amountOrigin.X + amount.ActualWidth <= row.ActualWidth + 0.1);
            });
            window.Close();
        });
    }

    [Fact]
    public void Multiple_obligations_remain_vertically_stacked_inside_receipt_width()
    {
        WpfTestHost.Invoke(() =>
        {
            var receipt = CreateReceipt() with
            {
                UnallocatedAmountMinor = 0,
                Allocations =
                [
                    new CustomerPaymentReceiptAllocationItem(
                        CustomerPaymentAllocationTargetType.Sale,
                        "VTE-REFERENCE-MULTI-TRES-LONGUE-0000000000001",
                        430_000,
                        40_000,
                        40_000,
                        390_000),
                    new CustomerPaymentReceiptAllocationItem(
                        CustomerPaymentAllocationTargetType.InitialCustomerReceivable,
                        "CRI-REFERENCE-MULTI-TRES-LONGUE-0000000000002",
                        80_000,
                        30_000,
                        30_000,
                        50_000)
                ]
            };
            var document = CustomerPaymentReceiptDocumentFactory.Create(receipt);
            var text = new TextRange(document.ContentStart, document.ContentEnd).Text;
            var rows = MoneyRows(document).ToArray();

            Assert.Contains("VTE-REFERENCE-MULTI-TRES-LONGUE-0000000000001", text);
            Assert.Contains("CRI-REFERENCE-MULTI-TRES-LONGUE-0000000000002", text);
            Assert.Equal(9, rows.Length);
            Assert.Empty(document.Blocks.OfType<Table>());
            Assert.All(rows, row =>
                Assert.Equal(CustomerPaymentReceiptDocumentFactory.ContentWidthDip, row.Width, 8));
        });
    }

    [Fact]
    public void Preview_has_print_and_exactly_one_close_action_and_scrolls_vertically()
    {
        WpfTestHost.Invoke(() =>
        {
            var window = new CustomerPaymentReceiptWindow(CreateReceipt(), new StubPrinter());
            window.Show();
            window.Measure(new Size(430, 720));
            window.Arrange(new Rect(0, 0, 430, 720));
            window.UpdateLayout();

            var buttons = FindVisualChildren<Button>(window).ToArray();
            Assert.Single(buttons, button => Equals(button.Content, "Fermer"));
            Assert.Single(buttons, button => Equals(button.Content, "Imprimer"));
            var preview = Assert.IsType<FlowDocumentScrollViewer>(window.FindName("ReceiptPreview"));
            Assert.Equal(ScrollBarVisibility.Auto, preview.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, preview.HorizontalScrollBarVisibility);
            window.Close();
        });
    }

    [Fact]
    public void Print_cancellation_and_gateway_failure_are_controlled_and_never_spool()
    {
        WpfTestHost.Invoke(() =>
        {
            var cancelledGateway = new StubPrintGateway(showDialogResult: false);
            var cancelledPrinter = new WpfCustomerPaymentReceiptPrinter(cancelledGateway);

            var cancelled = cancelledPrinter.Print(CreateReceipt());

            Assert.Equal(CustomerPaymentReceiptPrintOutcome.Cancelled, cancelled.Outcome);
            Assert.Contains("reste enregistré", cancelled.Message);
            Assert.Equal(0, cancelledGateway.PrintCount);

            var failingGateway = new StubPrintGateway(exception: new InvalidOperationException());
            var failingPrinter = new WpfCustomerPaymentReceiptPrinter(failingGateway);
            var failed = failingPrinter.Print(CreateReceipt());

            Assert.Equal(CustomerPaymentReceiptPrintOutcome.Failed, failed.Outcome);
            Assert.Contains("reste enregistré", failed.Message);
            Assert.Equal(0, failingGateway.PrintCount);
        });
    }

    [Fact]
    public void Supplier_view_model_does_not_enable_customer_receipts()
    {
        var supplierProperty = typeof(SupplierPaymentsViewModel)
            .GetProperty(nameof(PaymentManagementViewModelBase.SupportsCustomerReceipts));
        var customerProperty = typeof(CustomerPaymentsViewModel)
            .GetProperty(nameof(PaymentManagementViewModelBase.SupportsCustomerReceipts));

        Assert.NotNull(supplierProperty);
        Assert.Equal(typeof(PaymentManagementViewModelBase), supplierProperty!.DeclaringType);
        Assert.NotNull(customerProperty);
        Assert.Equal(typeof(CustomerPaymentsViewModel), customerProperty!.DeclaringType);
    }

    private static CustomerPaymentReceiptItem CreateReceipt(string? reference = null) => new(
        "ENC-RECEIPT-001",
        new DateOnly(2026, 9, 11),
        new DateTimeOffset(2026, 9, 11, 15, 40, 0, TimeSpan.Zero),
        "Al Amal Market avec un nom suffisamment long pour valider le retour à la ligne",
        100_000,
        PaymentMethod.Cash,
        30_000,
        [new CustomerPaymentReceiptAllocationItem(
            CustomerPaymentAllocationTargetType.Sale,
            reference ?? "VTE-001",
            430_000,
            70_000,
            400_000,
            30_000)]);

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private static IEnumerable<Grid> MoneyRows(FlowDocument document) =>
        document.Blocks
            .OfType<BlockUIContainer>()
            .Select(block => block.Child)
            .OfType<Grid>()
            .Where(grid => Equals(grid.Tag, CustomerPaymentReceiptDocumentFactory.MoneyRowTag));

    private static TextBlock MoneyLabel(Grid row) =>
        Assert.IsType<TextBlock>(row.Children.Cast<UIElement>().Single(child => Grid.GetColumn(child) == 0));

    private static TextBlock MoneyAmount(Grid row) =>
        Assert.IsType<TextBlock>(row.Children.Cast<UIElement>().Single(child => Grid.GetColumn(child) == 1));

    private sealed class StubPrinter : ICustomerPaymentReceiptPrinter
    {
        public CustomerPaymentReceiptPrintResult Print(CustomerPaymentReceiptItem receipt) =>
            new(CustomerPaymentReceiptPrintOutcome.Cancelled, "Annulé");
    }

    private sealed class StubPrintGateway(
        bool? showDialogResult = true,
        Exception? exception = null) : ICustomerPaymentPrintGateway
    {
        public int PrintCount { get; private set; }
        public double PrintableAreaWidth => CustomerPaymentReceiptDocumentFactory.PaperWidthDip;
        public double PrintableAreaHeight => 800;

        public bool? ShowDialog()
        {
            if (exception is not null)
                throw exception;
            return showDialogResult;
        }

        public void PrintDocument(DocumentPaginator paginator, string description) => PrintCount++;
    }
}
