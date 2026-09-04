using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Application.InitialCustomerReceivables;
using FinancialManagement.Application.InitialSupplierPayables;
using FinancialManagement.Application.Purchases;
using FinancialManagement.Application.Sales;
using FinancialManagement.Application.SupplierAdvances;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class FinancialDashboardServiceTests
{
    private static readonly DateOnly August31 = new(2026, 8, 31);
    private static readonly DateOnly September1 = new(2026, 9, 1);
    private static readonly DateOnly September30 = new(2026, 9, 30);
    private static readonly DateOnly October1 = new(2026, 10, 1);
    private static readonly DateTimeOffset Timestamp =
        new(2026, 9, 15, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Empty_state_is_zero_and_responsible_can_read_dashboard()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-empty", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.Dashboard.GetAsync(new FinancialDashboardQuery(null, null));

        Assert.True(result.Success);
        Assert.Equal(FinancialDashboardFailureReason.None, result.FailureReason);
        Assert.False(result.TreasuryIsInitialized);
        Assert.Equal(0, result.TotalSalesMinor);
        Assert.Equal(0, result.TotalPurchasesMinor);
        Assert.Equal(0, result.CustomerCollectionsMinor);
        Assert.Equal(0, result.SupplierPaymentsMinor);
        Assert.Equal(0, result.ExpensesMinor);
        Assert.Equal(0, result.ResultMinor);
        Assert.Equal(0, result.CurrentCustomerReceivablesMinor);
        Assert.Equal(0, result.CurrentSupplierPayablesMinor);
        Assert.Equal(0, result.CurrentCashBalanceMinor);
        Assert.Equal(0, result.CurrentBankBalanceMinor);
        Assert.Equal(0, result.CurrentTotalTreasuryMinor);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Employee_is_forbidden_even_with_all_configurable_permissions(
        bool grantAllPermissions)
    {
        using var database = new DashboardTestDatabase();
        var employee = database.AddUser("dashboard-employee-" + grantAllPermissions, UserRole.Employe);
        if (grantAllPermissions)
        {
            database.GrantAllModulePermissions(employee);
        }

        await database.AuthenticateAsync(employee);

        var result = await database.Dashboard.GetAsync(new FinancialDashboardQuery(null, null));

        Assert.False(result.Success);
        Assert.Equal(FinancialDashboardFailureReason.Forbidden, result.FailureReason);
    }

    [Fact]
    public async Task Missing_session_is_not_authenticated()
    {
        using var database = new DashboardTestDatabase();

        var result = await database.Dashboard.GetAsync(new FinancialDashboardQuery(null, null));

        Assert.False(result.Success);
        Assert.Equal(FinancialDashboardFailureReason.NotAuthenticated, result.FailureReason);
    }

    [Fact]
    public async Task Invalid_date_range_is_rejected_before_financial_queries()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-invalid-range", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September30, September1));

        Assert.False(result.Success);
        Assert.Equal(FinancialDashboardFailureReason.InvalidDateRange, result.FailureReason);
        Assert.Equal(September30, result.FilterStartDate);
        Assert.Equal(September1, result.FilterEndDate);
    }

    [Fact]
    public async Task Period_filters_are_inclusive_and_support_one_sided_bounds()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-boundaries", UserRole.Responsable);
        var client = database.AddClient("Boundary Client");
        var supplier = database.AddSupplier("Boundary Supplier");
        await database.AuthenticateAsync(user);

        foreach (var date in new[] { August31, September1, September30, October1 })
        {
            database.AddSale(user, client, date, 100);
            database.AddPurchase(user, supplier, date, 100);
            database.AddCustomerPayment(user, client, date, 100, PaymentMethod.Cash);
            database.AddSupplierPayment(user, supplier, date, 100, PaymentMethod.Cash);
            database.AddExpense(user, date, 100, PaymentMethod.Cash);
        }

        var bounded = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));
        var startOnly = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, null));
        var endOnly = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(null, September30));

        Assert.Equal(200, bounded.TotalSalesMinor);
        Assert.Equal(200, bounded.TotalPurchasesMinor);
        Assert.Equal(200, bounded.CustomerCollectionsMinor);
        Assert.Equal(200, bounded.SupplierPaymentsMinor);
        Assert.Equal(200, bounded.ExpensesMinor);
        Assert.Equal(3, startOnly.TotalSalesMinor / 100);
        Assert.Equal(3, endOnly.TotalSalesMinor / 100);
        Assert.Equal(3, startOnly.TotalPurchasesMinor / 100);
        Assert.Equal(3, endOnly.TotalPurchasesMinor / 100);
        Assert.Equal(3, startOnly.CustomerCollectionsMinor / 100);
        Assert.Equal(3, endOnly.CustomerCollectionsMinor / 100);
        Assert.Equal(3, startOnly.SupplierPaymentsMinor / 100);
        Assert.Equal(3, endOnly.SupplierPaymentsMinor / 100);
        Assert.Equal(3, startOnly.ExpensesMinor / 100);
        Assert.Equal(3, endOnly.ExpensesMinor / 100);
    }

    [Fact]
    public async Task Current_stocks_ignore_period_filter_while_flows_do_not()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-current-stock", UserRole.Responsable);
        var client = database.AddClient("Current Stock Client");
        var supplier = database.AddSupplier("Current Stock Supplier");
        await database.AuthenticateAsync(user);
        var sale = database.AddSale(user, client, new DateOnly(2026, 1, 15), 1_000);
        var purchase = database.AddPurchase(user, supplier, new DateOnly(2026, 1, 15), 1_000);
        database.AddCustomerPayment(user, client, September1, 600, PaymentMethod.Cash, sale.Id);
        database.AddSupplierPayment(user, supplier, September1, 600, PaymentMethod.Cash, purchase.Id);

        var result = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));

        Assert.Equal(0, result.TotalSalesMinor);
        Assert.Equal(0, result.TotalPurchasesMinor);
        Assert.Equal(600, result.CustomerCollectionsMinor);
        Assert.Equal(600, result.SupplierPaymentsMinor);
        Assert.Equal(400, result.CurrentCustomerReceivablesMinor);
        Assert.Equal(400, result.CurrentSupplierPayablesMinor);
    }

    [Fact]
    public async Task Payment_date_is_used_instead_of_sale_or_purchase_date()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-payment-date", UserRole.Responsable);
        var client = database.AddClient("Payment Date Client");
        var supplier = database.AddSupplier("Payment Date Supplier");
        await database.AuthenticateAsync(user);
        var sale = database.AddSale(user, client, August31, 1_000);
        var purchase = database.AddPurchase(user, supplier, August31, 1_000);
        database.AddCustomerPayment(user, client, September1, 600, PaymentMethod.Cash, sale.Id);
        database.AddSupplierPayment(user, supplier, September1, 600, PaymentMethod.Cash, purchase.Id);

        var result = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));

        Assert.Equal(0, result.TotalSalesMinor);
        Assert.Equal(0, result.TotalPurchasesMinor);
        Assert.Equal(600, result.CustomerCollectionsMinor);
        Assert.Equal(600, result.SupplierPaymentsMinor);
        Assert.Equal(400, result.CurrentCustomerReceivablesMinor);
        Assert.Equal(400, result.CurrentSupplierPayablesMinor);
    }

    [Fact]
    public async Task Result_uses_sales_minus_purchases_minus_expenses_only()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-result", UserRole.Responsable);
        var client = database.AddClient("Result Client");
        var supplier = database.AddSupplier("Result Supplier");
        await database.AuthenticateAsync(user);
        database.AddSale(user, client, September1, 10_000);
        database.AddPurchase(user, supplier, September1, 6_000);
        database.AddExpense(user, September1, 1_500, PaymentMethod.BankTransfer);
        database.AddCustomerPayment(user, client, September1, 8_000, PaymentMethod.Cash);
        database.AddSupplierPayment(user, supplier, September1, 4_000, PaymentMethod.Cash);

        var result = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));

        Assert.Equal(10_000, result.TotalSalesMinor);
        Assert.Equal(6_000, result.TotalPurchasesMinor);
        Assert.Equal(1_500, result.ExpensesMinor);
        Assert.Equal(8_000, result.CustomerCollectionsMinor);
        Assert.Equal(4_000, result.SupplierPaymentsMinor);
        Assert.Equal(2_500, result.ResultMinor);
    }

    [Fact]
    public async Task Initial_obligations_are_stocks_only_and_payments_reduce_them()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-initial-obligations", UserRole.Responsable);
        var client = database.AddClient("Initial Receivable Client");
        var supplier = database.AddSupplier("Initial Payable Supplier");
        await database.AuthenticateAsync(user);
        var receivable = database.AddInitialReceivable(user, client, 5_000);
        var payable = database.AddInitialPayable(user, supplier, 5_000);

        var before = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));
        database.AddCustomerPayment(user, client, September1, 2_000, PaymentMethod.Cash, null, receivable.Id);
        database.AddSupplierPayment(user, supplier, September1, 2_000, PaymentMethod.BankTransfer, null, payable.Id);
        var after = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));

        Assert.Equal(0, before.TotalSalesMinor);
        Assert.Equal(0, before.TotalPurchasesMinor);
        Assert.Equal(5_000, before.CurrentCustomerReceivablesMinor);
        Assert.Equal(5_000, before.CurrentSupplierPayablesMinor);
        Assert.Equal(2_000, after.CustomerCollectionsMinor);
        Assert.Equal(2_000, after.SupplierPaymentsMinor);
        Assert.Equal(3_000, after.CurrentCustomerReceivablesMinor);
        Assert.Equal(3_000, after.CurrentSupplierPayablesMinor);
    }

    [Fact]
    public async Task Advance_reallocation_does_not_double_count_customer_or_supplier_flows()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-advances", UserRole.Responsable);
        var client = database.AddClient("Advance Client");
        var supplier = database.AddSupplier("Advance Supplier");
        await database.AuthenticateAsync(user);
        var firstSale = database.AddSale(user, client, September1, 1_000);
        var secondSale = database.AddSale(user, client, September1, 300);
        var firstPurchase = database.AddPurchase(user, supplier, September1, 1_000);
        var secondPurchase = database.AddPurchase(user, supplier, September1, 300);
        var customerPayment = await database.CreateCustomerPaymentAsync(
            client,
            1_200,
            [new CustomerPaymentAllocationInput(firstSale.Id, null, 1_000)]);
        var supplierPayment = await database.CreateSupplierPaymentAsync(
            supplier,
            1_200,
            [new SupplierPaymentAllocationInput(firstPurchase.Id, null, 1_000)]);
        var before = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));

        var customerAdvance = await database.CustomerAdvanceService.ApplyAsync(
            new ApplyCustomerAdvanceCommand(
                customerPayment.CustomerPaymentId!.Value,
                [new CustomerAdvanceAllocationInput(secondSale.Id, null, 200)]));
        var supplierAdvance = await database.SupplierAdvanceService.ApplyAsync(
            new ApplySupplierAdvanceCommand(
                supplierPayment.SupplierPaymentId!.Value,
                [new SupplierAdvanceAllocationInput(secondPurchase.Id, null, 200)]));
        var after = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));

        Assert.True(customerAdvance.Success);
        Assert.True(supplierAdvance.Success);
        Assert.Equal(1_200, before.CustomerCollectionsMinor);
        Assert.Equal(1_200, after.CustomerCollectionsMinor);
        Assert.Equal(1_200, before.SupplierPaymentsMinor);
        Assert.Equal(1_200, after.SupplierPaymentsMinor);
        Assert.Equal(300, before.CurrentCustomerReceivablesMinor);
        Assert.Equal(100, after.CurrentCustomerReceivablesMinor);
        Assert.Equal(300, before.CurrentSupplierPayablesMinor);
        Assert.Equal(100, after.CurrentSupplierPayablesMinor);
    }

    [Fact]
    public async Task Anonymous_sale_with_exact_payment_has_no_current_receivable()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-anonymous-sale", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        var sale = database.AddSale(user, null, September1, 1_000);
        database.AddCustomerPayment(user, null, September1, 1_000, PaymentMethod.Cash, sale.Id);

        var result = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));

        Assert.Equal(1_000, result.TotalSalesMinor);
        Assert.Equal(1_000, result.CustomerCollectionsMinor);
        Assert.Equal(0, result.CurrentCustomerReceivablesMinor);
    }

    [Fact]
    public async Task Dashboard_treasury_values_match_treasury_core_without_affecting_result()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-treasury", UserRole.Responsable);
        var client = database.AddClient("Treasury Dashboard Client");
        await database.AuthenticateAsync(user);
        Assert.True((await database.Treasury.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(September1, 1_000, 2_000))).Success);
        database.AddCustomerPayment(user, client, September1, 500, PaymentMethod.Cash);
        database.AddExpense(user, September1, 100, PaymentMethod.BankTransfer);
        Assert.True((await database.Treasury.CreateTransferAsync(
            new CreateTreasuryTransferCommand(
                TreasuryAccount.Cash,
                TreasuryAccount.Bank,
                250,
                "Dashboard treasury"))).Success);

        var treasury = await database.Treasury.GetSummaryAsync();
        var dashboard = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));

        Assert.Equal(treasury.CashBalanceMinor, dashboard.CurrentCashBalanceMinor);
        Assert.Equal(treasury.BankBalanceMinor, dashboard.CurrentBankBalanceMinor);
        Assert.Equal(treasury.TotalTreasuryMinor, dashboard.CurrentTotalTreasuryMinor);
        Assert.True(dashboard.TreasuryIsInitialized);
        Assert.Equal(-100, dashboard.ResultMinor);
        Assert.Equal(0, dashboard.TotalSalesMinor);
        Assert.Equal(0, dashboard.TotalPurchasesMinor);
    }

    [Fact]
    public async Task Treasury_transfers_do_not_change_dashboard_result_or_operational_flows()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-transfer-result", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        Assert.True((await database.Treasury.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(September1, 1_000, 2_000))).Success);
        var before = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));
        Assert.True((await database.Treasury.CreateTransferAsync(
            new CreateTreasuryTransferCommand(
                TreasuryAccount.Cash,
                TreasuryAccount.Bank,
                300,
                null))).Success);
        var after = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));

        Assert.Equal(before.TotalSalesMinor, after.TotalSalesMinor);
        Assert.Equal(before.TotalPurchasesMinor, after.TotalPurchasesMinor);
        Assert.Equal(before.CustomerCollectionsMinor, after.CustomerCollectionsMinor);
        Assert.Equal(before.SupplierPaymentsMinor, after.SupplierPaymentsMinor);
        Assert.Equal(before.ExpensesMinor, after.ExpensesMinor);
        Assert.Equal(before.ResultMinor, after.ResultMinor);
    }

    [Fact]
    public async Task Dashboard_read_does_not_write_persisted_rows()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-read-only", UserRole.Responsable);
        var client = database.AddClient("Read Only Client");
        var supplier = database.AddSupplier("Read Only Supplier");
        await database.AuthenticateAsync(user);
        database.AddSale(user, client, September1, 1_000);
        database.AddPurchase(user, supplier, September1, 800);
        database.AddCustomerPayment(user, client, September1, 500, PaymentMethod.Cash);
        database.AddSupplierPayment(user, supplier, September1, 300, PaymentMethod.BankTransfer);
        database.AddExpense(user, September1, 100, PaymentMethod.Cash);
        Assert.True((await database.Treasury.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(September1, 1_000, 2_000))).Success);
        var before = database.CountPersistedRows();

        var result = await database.Dashboard.GetAsync(
            new FinancialDashboardQuery(September1, September30));

        Assert.True(result.Success);
        Assert.Equal(before, database.CountPersistedRows());
    }

    [Fact]
    public async Task Overallocated_sale_is_reported_as_technical_corruption()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-corrupt-sale", UserRole.Responsable);
        var client = database.AddClient("Corrupt Sale Client");
        await database.AuthenticateAsync(user);
        var sale = database.AddSale(user, client, September1, 100);
        database.AddCustomerPayment(user, client, September1, 150, PaymentMethod.Cash, sale.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Dashboard.GetAsync(new FinancialDashboardQuery(null, null)));

        Assert.Contains("Sale", exception.Message, StringComparison.Ordinal);
        Assert.Contains("overallocated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overallocated_purchase_is_reported_as_technical_corruption()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-corrupt-purchase", UserRole.Responsable);
        var supplier = database.AddSupplier("Corrupt Purchase Supplier");
        await database.AuthenticateAsync(user);
        var purchase = database.AddPurchase(user, supplier, September1, 100);
        database.AddSupplierPayment(user, supplier, September1, 150, PaymentMethod.Cash, purchase.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Dashboard.GetAsync(new FinancialDashboardQuery(null, null)));

        Assert.Contains("Purchase", exception.Message, StringComparison.Ordinal);
        Assert.Contains("overallocated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overallocated_initial_customer_receivable_is_reported_as_corruption()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-corrupt-receivable", UserRole.Responsable);
        var client = database.AddClient("Corrupt Receivable Client");
        await database.AuthenticateAsync(user);
        var receivable = database.AddInitialReceivable(user, client, 100);
        database.AddCustomerPayment(user, client, September1, 150, PaymentMethod.Cash, null, receivable.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Dashboard.GetAsync(new FinancialDashboardQuery(null, null)));

        Assert.Contains("InitialCustomerReceivable", exception.Message, StringComparison.Ordinal);
        Assert.Contains("overallocated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overallocated_initial_supplier_payable_is_reported_as_corruption()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-corrupt-payable", UserRole.Responsable);
        var supplier = database.AddSupplier("Corrupt Payable Supplier");
        await database.AuthenticateAsync(user);
        var payable = database.AddInitialPayable(user, supplier, 100);
        database.AddSupplierPayment(user, supplier, September1, 150, PaymentMethod.Cash, null, payable.Id);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Dashboard.GetAsync(new FinancialDashboardQuery(null, null)));

        Assert.Contains("InitialSupplierPayable", exception.Message, StringComparison.Ordinal);
        Assert.Contains("overallocated", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancellation_is_propagated_and_dashboard_does_not_write()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-cancel", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        var countsBefore = database.CountFinancialRows();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.Dashboard.GetAsync(new FinancialDashboardQuery(null, null), cancellation.Token));

        Assert.Equal(countsBefore, database.CountFinancialRows());
    }

    [Fact]
    public async Task Result_or_stock_overflow_remains_a_technical_error()
    {
        using var database = new DashboardTestDatabase();
        var user = database.AddUser("dashboard-overflow", UserRole.Responsable);
        var client = database.AddClient("Overflow Dashboard Client");
        await database.AuthenticateAsync(user);
        database.AddSale(user, client, September1, long.MaxValue);
        database.AddSale(user, client, September1, 1);

        var exception = await Assert.ThrowsAnyAsync<Exception>(() =>
            database.Dashboard.GetAsync(new FinancialDashboardQuery(September1, September30)));

        Assert.True(exception is SqliteException or OverflowException);
    }

    private sealed class DashboardTestDatabase : IDisposable
    {
        private const string Password = "Dashboard test password 42!";
        private readonly string databasePath;
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;
        private int referenceSequence;

        public DashboardTestDatabase()
        {
            databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-Dashboard-{Guid.NewGuid():N}.db");
            serviceProvider = new ServiceCollection()
                .AddInfrastructure($"Data Source={databasePath};Pooling=False")
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
            scope = serviceProvider.CreateScope();
            Context = scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
            Context.Database.Migrate();
            PasswordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            AuthenticationService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            Dashboard = scope.ServiceProvider.GetRequiredService<IFinancialDashboardService>();
            Treasury = scope.ServiceProvider.GetRequiredService<ITreasuryService>();
            CustomerPaymentService = scope.ServiceProvider.GetRequiredService<ICustomerPaymentService>();
            SupplierPaymentService = scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();
            CustomerAdvanceService = scope.ServiceProvider.GetRequiredService<ICustomerAdvanceService>();
            SupplierAdvanceService = scope.ServiceProvider.GetRequiredService<ISupplierAdvanceService>();
        }

        public FinancialManagementDbContext Context { get; }

        public IFinancialDashboardService Dashboard { get; }

        public ITreasuryService Treasury { get; }

        public ICustomerPaymentService CustomerPaymentService { get; }

        public ISupplierPaymentService SupplierPaymentService { get; }

        public ICustomerAdvanceService CustomerAdvanceService { get; }

        public ISupplierAdvanceService SupplierAdvanceService { get; }

        private IPasswordHasher PasswordHasher { get; }

        private IAuthenticationService AuthenticationService { get; }

        public User AddUser(string username, UserRole role)
        {
            var user = new User
            {
                FullName = $"Dashboard Test {username}",
                Username = username,
                PasswordHash = PasswordHasher.HashPassword(Password),
                Role = role,
                IsActive = true,
                CreatedAtUtc = Timestamp,
                UpdatedAtUtc = Timestamp
            };
            Context.Users.Add(user);
            Context.SaveChanges();
            return user;
        }

        public Client AddClient(string name)
        {
            var client = new Client
            {
                Name = name,
                CreatedAtUtc = Timestamp,
                UpdatedAtUtc = Timestamp
            };
            Context.Clients.Add(client);
            Context.SaveChanges();
            return client;
        }

        public Supplier AddSupplier(string name)
        {
            var supplier = new Supplier
            {
                Name = name,
                CreatedAtUtc = Timestamp,
                UpdatedAtUtc = Timestamp
            };
            Context.Suppliers.Add(supplier);
            Context.SaveChanges();
            return supplier;
        }

        public Sale AddSale(User user, Client? client, DateOnly date, long amountMinor)
        {
            var sale = new Sale
            {
                Reference = NextReference("VTE"),
                SaleDate = date,
                TotalAmountMinor = amountMinor,
                ClientId = client?.Id,
                CreatedByUserId = user.Id,
                CreatedAtUtc = Timestamp,
                UpdatedAtUtc = Timestamp
            };
            Context.Sales.Add(sale);
            Context.SaveChanges();
            return sale;
        }

        public Purchase AddPurchase(User user, Supplier supplier, DateOnly date, long amountMinor)
        {
            var purchase = new Purchase
            {
                Reference = NextReference("ACH"),
                PurchaseDate = date,
                TotalAmountMinor = amountMinor,
                SupplierId = supplier.Id,
                CreatedByUserId = user.Id,
                CreatedAtUtc = Timestamp,
                UpdatedAtUtc = Timestamp
            };
            Context.Purchases.Add(purchase);
            Context.SaveChanges();
            return purchase;
        }

        public void AddCustomerPayment(
            User user,
            Client? client,
            DateOnly date,
            long amountMinor,
            PaymentMethod method,
            long? saleId = null,
            long? receivableId = null)
        {
            var payment = new CustomerPayment
            {
                Reference = NextReference("ENC"),
                ClientId = client?.Id,
                PaymentDate = date,
                AmountMinor = amountMinor,
                PaymentMethod = method,
                CreatedByUserId = user.Id,
                CreatedAtUtc = Timestamp
            };
            Context.CustomerPayments.Add(payment);
            Context.SaveChanges();
            if (saleId.HasValue || receivableId.HasValue)
            {
                Context.CustomerPaymentAllocations.Add(new CustomerPaymentAllocation
                {
                    CustomerPaymentId = payment.Id,
                    SaleId = saleId,
                    InitialCustomerReceivableId = receivableId,
                    AmountMinor = amountMinor,
                    CreatedAtUtc = Timestamp
                });
                Context.SaveChanges();
            }
        }

        public void AddSupplierPayment(
            User user,
            Supplier supplier,
            DateOnly date,
            long amountMinor,
            PaymentMethod method,
            long? purchaseId = null,
            long? payableId = null)
        {
            var payment = new SupplierPayment
            {
                Reference = NextReference("DEC"),
                SupplierId = supplier.Id,
                PaymentDate = date,
                AmountMinor = amountMinor,
                PaymentMethod = method,
                CreatedByUserId = user.Id,
                CreatedAtUtc = Timestamp
            };
            Context.SupplierPayments.Add(payment);
            Context.SaveChanges();
            if (purchaseId.HasValue || payableId.HasValue)
            {
                Context.SupplierPaymentAllocations.Add(new SupplierPaymentAllocation
                {
                    SupplierPaymentId = payment.Id,
                    PurchaseId = purchaseId,
                    InitialSupplierPayableId = payableId,
                    AmountMinor = amountMinor,
                    CreatedAtUtc = Timestamp
                });
                Context.SaveChanges();
            }
        }

        public void AddExpense(User user, DateOnly date, long amountMinor, PaymentMethod method)
        {
            Context.Expenses.Add(new Expense
            {
                Reference = NextReference("DEP"),
                ExpenseDate = date,
                ExpenseCategoryId = 1,
                AmountMinor = amountMinor,
                PaymentMethod = method,
                CreatedByUserId = user.Id,
                CreatedAtUtc = Timestamp,
                UpdatedAtUtc = Timestamp
            });
            Context.SaveChanges();
        }

        public InitialCustomerReceivable AddInitialReceivable(User user, Client client, long amountMinor)
        {
            var receivable = new InitialCustomerReceivable
            {
                Reference = NextReference("RCP"),
                ClientId = client.Id,
                ReceivableDate = August31,
                InitialAmountMinor = amountMinor,
                CreatedByUserId = user.Id,
                CreatedAtUtc = Timestamp
            };
            Context.InitialCustomerReceivables.Add(receivable);
            Context.SaveChanges();
            return receivable;
        }

        public InitialSupplierPayable AddInitialPayable(User user, Supplier supplier, long amountMinor)
        {
            var payable = new InitialSupplierPayable
            {
                Reference = NextReference("PBL"),
                SupplierId = supplier.Id,
                PayableDate = August31,
                InitialAmountMinor = amountMinor,
                CreatedByUserId = user.Id,
                CreatedAtUtc = Timestamp
            };
            Context.InitialSupplierPayables.Add(payable);
            Context.SaveChanges();
            return payable;
        }

        public async Task<CreateCustomerPaymentResult> CreateCustomerPaymentAsync(
            Client client,
            long amountMinor,
            IReadOnlyCollection<CustomerPaymentAllocationInput> allocations)
        {
            var result = await CustomerPaymentService.CreateAsync(
                new CreateCustomerPaymentCommand(
                    client.Id,
                    amountMinor,
                    PaymentMethod.Cash,
                    null,
                    allocations));
            Assert.True(result.Success);
            return result;
        }

        public async Task<CreateSupplierPaymentResult> CreateSupplierPaymentAsync(
            Supplier supplier,
            long amountMinor,
            IReadOnlyCollection<SupplierPaymentAllocationInput> allocations)
        {
            var result = await SupplierPaymentService.CreateAsync(
                new CreateSupplierPaymentCommand(
                    supplier.Id,
                    amountMinor,
                    PaymentMethod.Cash,
                    null,
                    allocations));
            Assert.True(result.Success);
            return result;
        }

        public void GrantAllModulePermissions(User employee)
        {
            foreach (var module in Enum.GetValues<PermissionModule>())
            {
                Context.UserPermissions.Add(new UserPermission
                {
                    UserId = employee.Id,
                    Module = module,
                    CanView = true,
                    CanCreate = true,
                    CanModify = true
                });
            }

            Context.SaveChanges();
        }

        public async Task AuthenticateAsync(User user)
        {
            var result = await AuthenticationService.AuthenticateAsync(user.Username, Password);
            Assert.True(result.Success);
        }

        public (int Sales, int Purchases, int CustomerPayments, int SupplierPayments, int Expenses) CountFinancialRows() =>
            (Context.Sales.Count(), Context.Purchases.Count(), Context.CustomerPayments.Count(),
                Context.SupplierPayments.Count(), Context.Expenses.Count());

        public int[] CountPersistedRows() =>
        [
            Context.Users.Count(),
            Context.UserPermissions.Count(),
            Context.Clients.Count(),
            Context.Suppliers.Count(),
            Context.Sales.Count(),
            Context.CustomerPayments.Count(),
            Context.CustomerPaymentAllocations.Count(),
            Context.InitialCustomerReceivables.Count(),
            Context.Purchases.Count(),
            Context.SupplierPayments.Count(),
            Context.SupplierPaymentAllocations.Count(),
            Context.InitialSupplierPayables.Count(),
            Context.ExpenseCategories.Count(),
            Context.Expenses.Count(),
            Context.TreasuryOpeningBalances.Count(),
            Context.TreasuryTransfers.Count(),
            Context.ApplicationSettings.Count()
        ];

        private string NextReference(string prefix) =>
            $"{prefix}-DASHBOARD-{++referenceSequence:D4}";

        public void Dispose()
        {
            Context.Database.CloseConnection();
            scope.Dispose();
            serviceProvider.Dispose();

            foreach (var path in new[]
            {
                databasePath,
                $"{databasePath}-wal",
                $"{databasePath}-shm",
                $"{databasePath}-journal"
            })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
