using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FinancialManagement.Desktop.Views;

namespace FinancialManagement.Desktop.Tests;

public sealed class SearchEnterUxTests
{
    [Fact]
    public void Primary_management_search_fields_bind_enter_to_the_existing_button_command()
    {
        WpfTestHost.Invoke(() =>
        {
            AssertSearch(new PartyManagementView(), "PartySearchInput", "ApplySearchCommand");
            AssertSearch(new SalesView(), "SalesSearchInput", "ApplyFiltersCommand");
            AssertSearch(new PurchasesView(), "PurchasesSearchInput", "ApplyFiltersCommand");
            AssertSearch(new ExpensesView(), "ExpensesSearchInput", "ApplyFiltersCommand");
            AssertSearch(new PaymentManagementView(), "PaymentSearchInput", "ApplyFiltersCommand");
        });
    }

    [Fact]
    public void Scoped_lookup_search_fields_bind_enter_without_affecting_form_fields()
    {
        WpfTestHost.Invoke(() =>
        {
            AssertSearch(new SalesView(), "ClientSearchInput", "SearchClientsCommand");
            AssertSearch(new PurchasesView(), "SupplierSearchInput", "SearchSuppliersCommand");
            AssertSearch(new ExpensesView(), "EmployeeSearchInput", "SearchEmployeesCommand");

            var payments = Arrange(new PaymentManagementView());
            AssertSearch(payments, "PartySearchInput", "SearchPartiesCommand");
            AssertSearch(payments, "CreateObligationSearchInput", "SearchObligationsCommand");
            AssertSearch(payments, "AdvanceObligationSearchInput", "SearchObligationsCommand");

            var initialObligations = Arrange(new InitialObligationView());
            AssertSearch(initialObligations, "InitialFilterPartySearchInput", "SearchFilterPartiesCommand");
            AssertSearch(initialObligations, "PartySearchInput", "SearchPartiesCommand");

            Assert.Empty(Assert.IsType<TextBox>(payments.FindName("PaymentAmountInput")).InputBindings);
            Assert.Empty(Assert.IsType<TextBox>(payments.FindName("PaymentObservationInput")).InputBindings);

            var expenses = Arrange(new ExpensesView());
            Assert.Empty(Assert.IsType<TextBox>(expenses.FindName("ExpenseAmountInput")).InputBindings);
            Assert.Empty(Assert.IsType<TextBox>(expenses.FindName("ExpenseObservationInput")).InputBindings);

            var parties = Arrange(new PartyManagementView());
            Assert.Empty(Assert.IsType<TextBox>(parties.FindName("PartyNameInput")).InputBindings);
            Assert.Empty(Assert.IsType<TextBox>(parties.FindName("PartyPhoneInput")).InputBindings);
        });
    }

    [Fact]
    public void Enter_command_observes_the_latest_text_and_empty_query_without_focus_change()
    {
        WpfTestHost.Invoke(() =>
        {
            var harness = new SearchHarness();
            var view = Arrange(new PartyManagementView { DataContext = harness });
            var search = Assert.IsType<TextBox>(view.FindName("PartySearchInput"));
            var enter = Assert.Single(search.InputBindings.OfType<KeyBinding>());

            search.Text = "Maroc";
            Assert.Equal("Maroc", harness.SearchText);
            harness.PageNumber = 4;
            Assert.Same(harness.ApplySearchCommand, enter.Command);
            enter.Command.Execute(null);
            Assert.Equal("Maroc", harness.ExecutedSearchText);
            Assert.Equal(1, harness.PageNumber);

            search.Text = string.Empty;
            Assert.Equal(string.Empty, harness.SearchText);
            harness.PageNumber = 3;
            enter.Command.Execute(null);
            Assert.Equal(string.Empty, harness.ExecutedSearchText);
            Assert.Equal(1, harness.PageNumber);
            Assert.Equal(2, harness.ExecutionCount);
        });
    }

    private static void AssertSearch(FrameworkElement view, string textBoxName, string commandPath)
    {
        var arrangedView = Arrange(view);
        var textBox = Assert.IsType<TextBox>(arrangedView.FindName(textBoxName));
        var textBinding = Assert.IsType<Binding>(BindingOperations.GetBinding(textBox, TextBox.TextProperty));
        Assert.Equal(UpdateSourceTrigger.PropertyChanged, textBinding.UpdateSourceTrigger);

        var keyBinding = Assert.Single(textBox.InputBindings.OfType<KeyBinding>());
        Assert.Equal(Key.Enter, keyBinding.Key);
        Assert.Equal(
            commandPath,
            Assert.IsType<Binding>(BindingOperations.GetBinding(keyBinding, InputBinding.CommandProperty)).Path.Path);

        Assert.Contains(
            FindVisualChildren<Button>(arrangedView),
            button => BindingOperations.GetBinding(button, Button.CommandProperty)?.Path.Path == commandPath);
    }

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

    private sealed class SearchHarness
    {
        public SearchHarness() => ApplySearchCommand = new HarnessCommand(ExecuteSearch);

        public string SearchText { get; set; } = string.Empty;
        public ICommand ApplySearchCommand { get; }
        public string? ExecutedSearchText { get; private set; }
        public int ExecutionCount { get; private set; }
        public int PageNumber { get; set; } = 1;

        private void ExecuteSearch()
        {
            ExecutedSearchText = SearchText;
            ExecutionCount++;
            PageNumber = 1;
        }
    }

    private sealed class HarnessCommand(Action execute) : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public bool CanExecute(object? parameter) => true;
        public void Execute(object? parameter) => execute();
    }
}
