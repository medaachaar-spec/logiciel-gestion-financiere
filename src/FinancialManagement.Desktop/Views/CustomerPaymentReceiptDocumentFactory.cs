using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.Views;

internal static class CustomerPaymentReceiptDocumentFactory
{
    internal const double DipPerMillimetre = 96d / 25.4d;
    internal const double PaperWidthMillimetres = 80d;
    internal const double ContentWidthMillimetres = 72d;
    internal const double PaperWidthDip = PaperWidthMillimetres * DipPerMillimetre;
    internal const double ContentWidthDip = ContentWidthMillimetres * DipPerMillimetre;
    internal const double MarginDip = (PaperWidthMillimetres - ContentWidthMillimetres) / 2d * DipPerMillimetre;
    internal const double MinimumPageHeightDip = 400d;
    internal const double MinimumMoneyLabelWidthDip = 112d;
    internal const string MoneyRowTag = "ReceiptMoneyRow";

    internal static FlowDocument Create(CustomerPaymentReceiptItem receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var document = new FlowDocument
        {
            PageWidth = PaperWidthDip,
            PagePadding = new Thickness(MarginDip),
            ColumnWidth = double.PositiveInfinity,
            FontFamily = new FontFamily("Arial"),
            FontSize = 12.5,
            Foreground = Brushes.Black,
            Background = Brushes.White,
            TextAlignment = TextAlignment.Left
        };

        document.Blocks.Add(Paragraph("EL BARKA EMBALLAGE", 16, FontWeights.Bold, TextAlignment.Center, 0));
        document.Blocks.Add(Paragraph("REÇU D'ENCAISSEMENT", 13.5, FontWeights.Bold, TextAlignment.Center, 3));
        document.Blocks.Add(Separator());
        document.Blocks.Add(Paragraph("N°", 11.5, FontWeights.Bold));
        document.Blocks.Add(Paragraph(receipt.PaymentReference, 12.5, FontWeights.Bold));
        document.Blocks.Add(Paragraph(
            $"Date : {receipt.PaymentDate:dd/MM/yyyy}  {receipt.PaymentCreatedAtUtc.ToLocalTime():HH:mm}"));
        document.Blocks.Add(Paragraph("Client", 11.5, FontWeights.Bold, marginTop: 8));
        document.Blocks.Add(Paragraph(receipt.CustomerName, 13, FontWeights.SemiBold));

        for (var index = 0; index < receipt.Allocations.Count; index++)
        {
            var allocation = receipt.Allocations[index];
            document.Blocks.Add(Separator(8));
            var type = allocation.TargetType == CustomerPaymentAllocationTargetType.Sale
                ? "Vente"
                : "Créance initiale";
            document.Blocks.Add(Paragraph(type, 11.5, FontWeights.Bold));
            document.Blocks.Add(Paragraph(allocation.TargetReference, 12.5, FontWeights.Bold));
            AddAmountRows(document,
                (allocation.TargetType == CustomerPaymentAllocationTargetType.Sale
                    ? "Montant vente"
                    : "Montant créance", allocation.OriginalObligationAmountMinor, false),
                ("Cet encaissement", allocation.AllocatedAmountMinor, true),
                ("Total payé", allocation.CumulativePaidAfterAllocationMinor, false),
                ("Reste à payer", allocation.RemainingAfterAllocationMinor, true));
        }

        document.Blocks.Add(Separator(8));
        if (receipt.Allocations.Count != 1 || receipt.UnallocatedAmountMinor > 0)
            AddAmountRows(document, ("Total encaissé", receipt.PaymentAmountMinor, true));
        if (receipt.UnallocatedAmountMinor > 0)
            AddAmountRows(document, ("Avance non affectée", receipt.UnallocatedAmountMinor, true));
        document.Blocks.Add(Paragraph(
            $"Mode : {FormatPaymentMethod(receipt.PaymentMethod)}",
            12.5,
            FontWeights.Bold,
            marginTop: 6));
        document.Blocks.Add(Separator(8));
        document.Blocks.Add(Paragraph("Merci.", 12.5, FontWeights.Normal, TextAlignment.Center));
        document.Blocks.Add(Paragraph("Tresoria", 10.5, FontWeights.SemiBold, TextAlignment.Center));
        return document;
    }

    private static string FormatPaymentMethod(PaymentMethod paymentMethod) => paymentMethod switch
    {
        PaymentMethod.Cash => "Espèces",
        PaymentMethod.BankTransfer => "Virement",
        _ => "Mode inconnu"
    };

    private static Paragraph Paragraph(
        string text,
        double fontSize = 12.5,
        FontWeight? fontWeight = null,
        TextAlignment textAlignment = TextAlignment.Left,
        double marginTop = 1)
    {
        var paragraph = new Paragraph(new Run(text))
        {
            FontSize = fontSize,
            FontWeight = fontWeight ?? FontWeights.Normal,
            TextAlignment = textAlignment,
            Margin = new Thickness(0, marginTop, 0, 1),
            LineHeight = fontSize * 1.22,
            TextIndent = 0
        };
        return paragraph;
    }

    private static Block Separator(double marginTop = 5) => new Paragraph(new Run(new string('-', 38)))
    {
        FontFamily = new FontFamily("Consolas"),
        FontSize = 10,
        TextAlignment = TextAlignment.Center,
        Margin = new Thickness(0, marginTop, 0, 3),
        LineHeight = 11
    };

    private static void AddAmountRows(
        FlowDocument document,
        params (string Label, long AmountMinor, bool Bold)[] rows)
    {
        foreach (var row in rows)
        {
            var weight = row.Bold ? FontWeights.Bold : FontWeights.Normal;
            var grid = new Grid
            {
                Width = ContentWidthDip,
                MinWidth = ContentWidthDip,
                MaxWidth = ContentWidthDip,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 1, 0, 1),
                Tag = MoneyRowTag
            };
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
                MinWidth = MinimumMoneyLabelWidthDip
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = new TextBlock
            {
                Text = row.Label,
                FontFamily = new FontFamily("Arial"),
                FontSize = 12.5,
                FontWeight = weight,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 8, 1),
                Tag = "ReceiptMoneyLabel"
            };
            var amount = new TextBlock
            {
                Text = MoneyFormatter.FormatMinor(row.AmountMinor),
                FontFamily = new FontFamily("Arial"),
                FontSize = 12.5,
                FontWeight = weight,
                Foreground = Brushes.Black,
                TextWrapping = TextWrapping.NoWrap,
                TextAlignment = TextAlignment.Right,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 1, 0, 1),
                Tag = "ReceiptMoneyAmount"
            };
            Grid.SetColumn(amount, 1);
            grid.Children.Add(label);
            grid.Children.Add(amount);
            TextOptions.SetTextFormattingMode(grid, TextFormattingMode.Display);

            document.Blocks.Add(new BlockUIContainer(grid)
            {
                Margin = new Thickness(0)
            });
        }
    }
}
