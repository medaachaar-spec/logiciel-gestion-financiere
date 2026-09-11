using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using FinancialManagement.Desktop.Views;

namespace FinancialManagement.Desktop.Tests;

public sealed class ModalCancelConsistencyTests
{
    [Fact]
    public void Create_and_edit_dialogs_keep_one_footer_cancel_and_their_primary_action()
    {
        WpfTestHost.Invoke(() =>
        {
            var expenses = Arrange(new ExpensesView());
            AssertDialog(
                Assert.IsType<Border>(expenses.FindName("ExpenseCreateDialog")),
                "CancelCreateFormCommand",
                "Enregistrer",
                "CreateExpenseCommand");

            var parties = Arrange(new PartyManagementView());
            AssertDialogWithBoundPrimary(
                Assert.IsType<Border>(parties.FindName("PartyFormDialog")),
                "CancelFormCommand",
                "SaveButtonLabel",
                "SaveCommand");

            var payments = Arrange(new PaymentManagementView());
            AssertDialog(
                Assert.IsType<Border>(payments.FindName("PaymentCreateDialog")),
                "CancelCreateCommand",
                "Vérifier et continuer",
                "PrepareCreateCommand");
            AssertDialog(
                Assert.IsType<Border>(payments.FindName("PaymentAdvanceDialog")),
                "CancelAdvanceCommand",
                "Affecter l'avance",
                "ApplyAdvanceCommand");

            var sales = Arrange(new SalesView());
            AssertDialog(
                Assert.IsType<Border>(sales.FindName("SaleCreateDialog")),
                "CancelCreateFormCommand",
                "Enregistrer",
                "CreateSaleCommand");

            var purchases = Arrange(new PurchasesView());
            AssertDialog(
                Assert.IsType<Border>(purchases.FindName("PurchaseCreateDialog")),
                "CancelCreateFormCommand",
                "Enregistrer",
                "CreatePurchaseCommand");

            var initialObligations = Arrange(new InitialObligationView());
            AssertDialog(
                Assert.IsType<Border>(initialObligations.FindName("InitialObligationCreateDialog")),
                "CancelCreateCommand",
                "Enregistrer",
                "SaveCommand");
        });
    }

    [Fact]
    public void Read_only_payment_detail_keeps_one_footer_close_action()
    {
        WpfTestHost.Invoke(() =>
        {
            var payments = Arrange(new PaymentManagementView());
            var dialog = Assert.IsType<Border>(payments.FindName("PaymentDetailDialog"));
            var close = Assert.Single(Buttons(dialog), button => Equals(button.Content, "Fermer"));

            Assert.Equal(Visibility.Visible, close.Visibility);
            AssertCommand(close, "CloseDetailCommand");
        });
    }

    private static void AssertDialog(
        DependencyObject dialog,
        string cancelCommand,
        string primaryContent,
        string primaryCommand)
    {
        var buttons = Buttons(dialog);
        var cancel = Assert.Single(buttons, button => Equals(button.Content, "Annuler"));
        var primary = Assert.Single(buttons, button => Equals(button.Content, primaryContent));

        Assert.Equal(Visibility.Visible, cancel.Visibility);
        AssertCommand(cancel, cancelCommand);
        AssertCommand(primary, primaryCommand);
    }

    private static void AssertDialogWithBoundPrimary(
        DependencyObject dialog,
        string cancelCommand,
        string primaryContentBinding,
        string primaryCommand)
    {
        var buttons = Buttons(dialog);
        var cancel = Assert.Single(buttons, button => Equals(button.Content, "Annuler"));
        var primary = Assert.Single(buttons, button =>
            BindingOperations.GetBinding(button, ContentControl.ContentProperty)?.Path.Path
                == primaryContentBinding);

        Assert.Equal(Visibility.Visible, cancel.Visibility);
        AssertCommand(cancel, cancelCommand);
        AssertCommand(primary, primaryCommand);
    }

    private static void AssertCommand(Button button, string path) =>
        Assert.Equal(
            path,
            Assert.IsType<Binding>(BindingOperations.GetBinding(button, Button.CommandProperty)).Path.Path);

    private static Button[] Buttons(DependencyObject parent) =>
        FindVisualChildren<Button>(parent).ToArray();

    private static T Arrange<T>(T view)
        where T : FrameworkElement
    {
        view.Measure(new Size(1100, 700));
        view.Arrange(new Rect(0, 0, 1100, 700));
        view.UpdateLayout();
        return view;
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
