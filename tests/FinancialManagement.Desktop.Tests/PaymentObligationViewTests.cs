using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Globalization;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Views;

namespace FinancialManagement.Desktop.Tests;

public sealed class PaymentObligationViewTests
{
    [Fact]
    public void Main_list_is_obligation_centric_and_financial_columns_are_right_aligned()
    {
        WpfTestHost.Invoke(() =>
        {
            var view = ArrangeView();
            var grid = Assert.IsType<DataGrid>(view.FindName("PaymentObligationDataGrid"));

            Assert.Equal(8, grid.Columns.Count);
            Assert.Equal("Date", grid.Columns[0].Header);
            Assert.Equal("Montant", grid.Columns[3].Header);
            Assert.Equal("Payé", grid.Columns[4].Header);
            Assert.Equal("Reste à payer", grid.Columns[5].Header);
            Assert.Equal("Statut", grid.Columns[6].Header);
            Assert.Equal("Actions", grid.Columns[7].Header);
            AssertBinding(grid.Columns[0], "BusinessDate");
            AssertBinding(grid.Columns[3], "OriginalAmount");
            AssertBinding(grid.Columns[4], "PaidAmount");
            AssertBinding(grid.Columns[5], "RemainingAmount");
            AssertBinding(grid.Columns[6], "Status");

            foreach (var index in new[] { 3, 4, 5 })
            {
                var column = Assert.IsType<DataGridTextColumn>(grid.Columns[index]);
                Assert.Equal(
                    TextAlignment.Right,
                    column.ElementStyle.Setters
                        .OfType<Setter>()
                        .Single(setter => setter.Property == TextBlock.TextAlignmentProperty)
                        .Value);
                Assert.Equal(
                    HorizontalAlignment.Right,
                    Assert.IsType<Style>(column.HeaderStyle).Setters
                        .OfType<Setter>()
                        .Single(setter => setter.Property == Control.HorizontalContentAlignmentProperty)
                        .Value);
            }

            var referenceColumn = Assert.IsType<DataGridTemplateColumn>(grid.Columns[2]);
            var referenceContent = Assert.IsType<StackPanel>(referenceColumn.CellTemplate.LoadContent());
            var referenceText = Assert.IsType<TextBlock>(referenceContent.Children[0]);
            Assert.Equal(TextTrimming.CharacterEllipsis, StyleValue(referenceText.Style, TextBlock.TextTrimmingProperty));
            Assert.IsType<Binding>(StyleValue(referenceText.Style, FrameworkElement.ToolTipProperty));
        });
    }

    [Fact]
    public void Detail_has_compact_history_columns_and_exactly_one_close_action()
    {
        WpfTestHost.Invoke(() =>
        {
            var view = ArrangeView();
            var history = Assert.IsType<DataGrid>(view.FindName("PaymentHistoryDataGrid"));

            Assert.Equal(6, history.Columns.Count);
            Assert.Equal("Date / heure", history.Columns[0].Header);
            Assert.Equal("Référence paiement", history.Columns[1].Header);
            Assert.Equal("Mode", history.Columns[2].Header);
            Assert.Equal("Montant affecté", history.Columns[3].Header);
            Assert.Equal("Cumul payé", history.Columns[4].Header);
            Assert.Equal("Reste après paiement", history.Columns[5].Header);
            Assert.Equal(38d, history.RowHeight);
            Assert.Equal(38d, history.ColumnHeaderHeight);
            Assert.Equal(DataGridGridLinesVisibility.Horizontal, history.GridLinesVisibility);
            Assert.Equal(266d, history.MaxHeight);
            Assert.Equal(ScrollBarVisibility.Disabled, ScrollViewer.GetHorizontalScrollBarVisibility(history));

            var detailDialog = Assert.IsType<Border>(view.FindName("PaymentDetailDialog"));
            Assert.Equal(VerticalAlignment.Center, detailDialog.VerticalAlignment);

            var partyLabel = Assert.IsType<TextBlock>(view.FindName("PaymentDetailPartyLabel"));
            Assert.Equal(
                "DetailPartyLabel",
                Assert.IsType<Binding>(BindingOperations.GetBinding(partyLabel, TextBlock.TextProperty)).Path.Path);

            var paymentReference = Assert.IsType<DataGridTextColumn>(history.Columns[1]);
            Assert.Equal(
                TextTrimming.CharacterEllipsis,
                StyleValue(paymentReference.ElementStyle, TextBlock.TextTrimmingProperty));
            Assert.IsType<Binding>(StyleValue(paymentReference.ElementStyle, FrameworkElement.ToolTipProperty));

            foreach (var index in new[] { 3, 4, 5 })
            {
                var column = Assert.IsType<DataGridTextColumn>(history.Columns[index]);
                Assert.Equal(
                    TextAlignment.Right,
                    StyleValue(column.ElementStyle, TextBlock.TextAlignmentProperty));
                Assert.Equal(
                    HorizontalAlignment.Right,
                    StyleValue(column.HeaderStyle, Control.HorizontalContentAlignmentProperty));
            }

            var closeButtons = FindVisualChildren<Button>(view)
                .Where(button => Equals(button.Content, "Fermer"))
                .ToArray();
            Assert.Single(closeButtons);
        });
    }

    [Fact]
    public void Accepted_filter_controls_are_preserved_and_money_formatter_groups_thousands()
    {
        WpfTestHost.Invoke(() =>
        {
            var view = ArrangeView();

            var fromDate = Assert.IsType<DatePicker>(view.FindName("PaymentFromDateInput"));
            var toDate = Assert.IsType<DatePicker>(view.FindName("PaymentToDateInput"));
            var status = Assert.IsType<ComboBox>(view.FindName("PaymentStatusFilter"));

            Assert.Same(view.FindResource("InputDatePickerStyle"), fromDate.Style);
            Assert.Same(fromDate.Style, toDate.Style);
            Assert.Same(view.FindResource("InputComboBoxStyle"), status.Style);
            Assert.Equal("FilterStatuses", Assert.IsType<Binding>(BindingOperations.GetBinding(status, ItemsControl.ItemsSourceProperty)).Path.Path);
            Assert.Equal("SelectedFilterStatus", Assert.IsType<Binding>(BindingOperations.GetBinding(status, Selector.SelectedItemProperty)).Path.Path);

            var separator = CultureInfo.GetCultureInfo("fr-FR").NumberFormat.NumberGroupSeparator;
            Assert.Equal($"4{separator}300,00", MoneyFormatter.FormatMinor(430_000));
            Assert.Equal($"24{separator}580,00", MoneyFormatter.FormatMinor(2_458_000));
        });
    }

    [Fact]
    public void Sidebar_uses_keyboard_focus_visual_instead_of_an_always_focused_border()
    {
        WpfTestHost.Invoke(() =>
        {
            var view = ArrangeView();
            var navigationStyle = Assert.IsType<Style>(view.FindResource("NavigationButtonStyle"));

            Assert.IsType<Style>(navigationStyle.Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == FrameworkElement.FocusVisualStyleProperty)
                .Value);

            var template = Assert.IsType<ControlTemplate>(navigationStyle.Setters
                .OfType<Setter>()
                .Single(setter => setter.Property == Control.TemplateProperty)
                .Value);
            Assert.DoesNotContain(
                template.Triggers.OfType<Trigger>(),
                trigger => trigger.Property == UIElement.IsKeyboardFocusedProperty);
        });
    }

    private static PaymentManagementView ArrangeView()
    {
        var view = new PaymentManagementView();
        view.Measure(new Size(1100, 700));
        view.Arrange(new Rect(0, 0, 1100, 700));
        view.UpdateLayout();
        return view;
    }

    private static void AssertBinding(DataGridColumn column, string path)
    {
        var textColumn = Assert.IsType<DataGridTextColumn>(column);
        Assert.Equal(path, Assert.IsType<Binding>(textColumn.Binding).Path.Path);
    }

    private static object StyleValue(Style style, DependencyProperty property)
    {
        for (var candidate = style; candidate is not null; candidate = candidate.BasedOn)
        {
            var setter = candidate.Setters
                .OfType<Setter>()
                .FirstOrDefault(item => item.Property == property);
            if (setter is not null)
                return setter.Value;
        }

        throw new Xunit.Sdk.XunitException($"No setter found for {property.Name}.");
    }

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
}
