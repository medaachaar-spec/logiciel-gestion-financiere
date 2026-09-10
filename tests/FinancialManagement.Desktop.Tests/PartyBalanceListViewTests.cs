using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Desktop.ViewModels;
using FinancialManagement.Desktop.Views;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Tests;

namespace FinancialManagement.Desktop.Tests;

public sealed class PartyBalanceListViewTests
{
    [Fact]
    public async Task Returning_to_party_lists_reloads_current_balances()
    {
        using var database = new PartyCrudTestDatabase();
        var responsible = database.AddUser("party-list-reentry", UserRole.Responsable);
        var client = database.AddClient("Client");
        var supplier = database.AddSupplier("Supplier");
        database.AddSale(client, responsible, 100_000);
        database.AddPurchase(supplier, responsible, 200_000);
        await database.AuthenticateAsync(responsible);

        using var coordinator = new DashboardOperationCoordinator();
        var clients = new ClientsViewModel(
            database.ClientService,
            database.Get<ICurrentUserSession>(),
            database.Get<IAuthorizationService>(),
            coordinator);
        var suppliers = new SuppliersViewModel(
            database.SupplierService,
            database.Get<ICurrentUserSession>(),
            database.Get<IAuthorizationService>(),
            coordinator);

        await clients.LoadAsync();
        await suppliers.LoadAsync();
        Assert.Equal(100_000, Assert.Single(clients.Items).CurrentBalanceMinorUnits);
        Assert.Equal(200_000, Assert.Single(suppliers.Items).CurrentBalanceMinorUnits);

        database.AddSale(client, responsible, 30_000);
        database.AddPurchase(supplier, responsible, 40_000);

        await clients.LoadAsync();
        await suppliers.LoadAsync();
        Assert.Equal(130_000, Assert.Single(clients.Items).CurrentBalanceMinorUnits);
        Assert.Equal(240_000, Assert.Single(suppliers.Items).CurrentBalanceMinorUnits);
    }

    [Theory]
    [InlineData("Créance actuelle", "Client", "créance actuelle", 0, "0,00")]
    [InlineData("Dette actuelle", "Fournisseur", "dette actuelle", 12_345, "123,45")]
    public void Party_list_renders_balance_instead_of_identifier(
        string columnHeader,
        string partyKind,
        string balanceLabel,
        long amountMinor,
        string expectedDisplay)
    {
        WpfTestHost.Invoke(() =>
        {
            var item = new PartyListItemViewModel(
                42,
                "Atlas",
                "0600000000",
                "Atlas SARL",
                "TECH-42",
                amountMinor,
                partyKind,
                balanceLabel);
            var view = new PartyManagementView
            {
                DataContext = new PartyListVisualModel(columnHeader, item)
            };
            view.Measure(new Size(1100, 650));
            view.Arrange(new Rect(0, 0, 1100, 650));
            view.UpdateLayout();

            var grid = Assert.IsType<DataGrid>(view.FindName("PartyDataGrid"));
            Assert.False(grid.CanUserSortColumns);
            Assert.Equal(5, grid.Columns.Count);
            Assert.Equal("Nom", grid.Columns[0].Header);
            Assert.Equal("Téléphone", grid.Columns[1].Header);
            Assert.Equal("Société", grid.Columns[2].Header);
            Assert.Equal(columnHeader, Assert.IsType<TextBlock>(grid.Columns[3].Header).Text);
            Assert.Equal("Actions", grid.Columns[4].Header);
            Assert.DoesNotContain(grid.Columns, column => Equals(column.Header, "Identifiant"));

            var balanceColumn = Assert.IsType<DataGridTextColumn>(grid.Columns[3]);
            var binding = Assert.IsType<Binding>(balanceColumn.Binding);
            Assert.Equal(nameof(PartyListItemViewModel.CurrentBalanceDisplay), binding.Path.Path);
            Assert.Equal(
                TextAlignment.Right,
                balanceColumn.ElementStyle.Setters
                    .OfType<Setter>()
                    .Single(setter => setter.Property == TextBlock.TextAlignmentProperty)
                    .Value);

            Assert.Equal(amountMinor, item.CurrentBalanceMinorUnits);
            Assert.Equal(expectedDisplay, item.CurrentBalanceDisplay);
            Assert.Equal("TECH-42", item.IdentifierDisplay);
            Assert.DoesNotContain("TECH-42", item.AccessibleDescription, StringComparison.Ordinal);
            Assert.Contains($"{balanceLabel} {expectedDisplay}", item.AccessibleDescription, StringComparison.Ordinal);
        });
    }

    private sealed class PartyListVisualModel
    {
        public PartyListVisualModel(string balanceColumnHeader, PartyListItemViewModel item)
        {
            BalanceColumnHeader = balanceColumnHeader;
            Items = [item];
        }

        public string BalanceColumnHeader { get; }

        public ObservableCollection<PartyListItemViewModel> Items { get; }

        public bool HasItems => true;

        public bool IsOverlayVisible => false;

        public bool IsLoading => false;

        public bool IsLoadingDetail => false;

        public bool HasSuccessMessage => false;

        public bool HasListError => false;

        public bool ShowEmptyState => false;

        public bool CanCreate => false;

        public string PageTitle => "Parties";

        public string PageDescription => "Test";
    }
}
