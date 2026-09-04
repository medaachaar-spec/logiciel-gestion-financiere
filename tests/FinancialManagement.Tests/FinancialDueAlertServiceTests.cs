using FinancialManagement.Application.Alerts;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Application.SupplierAdvances;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class FinancialDueAlertServiceTests
{
    private static readonly DateOnly CurrentDate = new(2026, 9, 10);
    private static readonly DateTimeOffset CurrentUtc =
        new(2026, 9, 9, 23, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task Empty_state_succeeds_with_no_alerts()
    {
        using var database = new AlertTestDatabase();
        var user = database.AddUser("alert-empty", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());

        Assert.True(result.Success);
        Assert.Equal(CurrentDate, result.CurrentDate);
        Assert.Empty(result.CustomerAlerts);
        Assert.Empty(result.SupplierAlerts);
        Assert.Equal(0, result.TotalCustomerAlertCount);
        Assert.Equal(0, result.TotalSupplierAlertCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Employee_is_forbidden_even_with_all_permissions(bool grantAllPermissions)
    {
        using var database = new AlertTestDatabase();
        var employee = database.AddUser("alert-employee-" + grantAllPermissions, UserRole.Employe);
        if (grantAllPermissions)
        {
            database.GrantAllModulePermissions(employee);
        }

        await database.AuthenticateAsync(employee);
        var result = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());

        Assert.False(result.Success);
        Assert.Equal(FinancialDueAlertsFailureReason.Forbidden, result.FailureReason);
    }

    [Fact]
    public async Task Missing_session_returns_not_authenticated()
    {
        using var database = new AlertTestDatabase();

        var result = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());

        Assert.False(result.Success);
        Assert.Equal(FinancialDueAlertsFailureReason.NotAuthenticated, result.FailureReason);
    }

    [Fact]
    public async Task J5_boundary_and_temporal_statuses_are_derived_from_time_provider()
    {
        using var database = new AlertTestDatabase();
        var user = database.AddUser("alert-dates", UserRole.Responsable);
        var client = database.AddClient("Date Alert Client");
        await database.AuthenticateAsync(user);
        database.AddSale(user, client, "SALE-OVERDUE-01", new DateOnly(2026, 9, 1), 100);
        database.AddSale(user, client, "SALE-OVERDUE-02", new DateOnly(2026, 9, 9), 100);
        database.AddSale(user, client, "SALE-TODAY", CurrentDate, 100);
        database.AddSale(user, client, "SALE-UPCOMING-02", new DateOnly(2026, 9, 12), 100);
        database.AddSale(user, client, "SALE-UPCOMING-05", new DateOnly(2026, 9, 15), 100);
        database.AddSale(user, client, "SALE-OUTSIDE-J5", new DateOnly(2026, 9, 16), 100);

        var result = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());

        Assert.Equal(
            [
                "SALE-OVERDUE-01",
                "SALE-OVERDUE-02",
                "SALE-TODAY",
                "SALE-UPCOMING-02",
                "SALE-UPCOMING-05"
            ],
            result.CustomerAlerts.Select(alert => alert.Reference));
        var overdue = result.CustomerAlerts[0];
        Assert.Equal(-9, overdue.DaysUntilDue);
        Assert.Equal(FinancialDueAlertStatus.Overdue, overdue.Status);
        Assert.Equal(0, result.CustomerAlerts[2].DaysUntilDue);
        Assert.Equal(FinancialDueAlertStatus.DueToday, result.CustomerAlerts[2].Status);
        Assert.Equal(5, result.CustomerAlerts[4].DaysUntilDue);
        Assert.Equal(FinancialDueAlertStatus.Upcoming, result.CustomerAlerts[4].Status);
        Assert.DoesNotContain(result.CustomerAlerts, alert => alert.Reference == "SALE-OUTSIDE-J5");
    }

    [Fact]
    public async Task Same_due_date_is_sorted_by_reference_and_due_date_null_is_excluded()
    {
        using var database = new AlertTestDatabase();
        var user = database.AddUser("alert-sort", UserRole.Responsable);
        var client = database.AddClient("Sort Alert Client");
        var supplier = database.AddSupplier("Sort Alert Supplier");
        await database.AuthenticateAsync(user);
        database.AddSale(user, client, "SALE-Z", new DateOnly(2026, 9, 12), 100);
        database.AddSale(user, client, "SALE-A", new DateOnly(2026, 9, 12), 100);
        database.AddSale(user, client, "SALE-NO-DUE", null, 100);
        database.AddPurchase(user, supplier, "PURCHASE-Z", new DateOnly(2026, 9, 12), 100);
        database.AddPurchase(user, supplier, "PURCHASE-A", new DateOnly(2026, 9, 12), 100);
        database.AddPurchase(user, supplier, "PURCHASE-NO-DUE", null, 100);

        var result = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());

        Assert.Equal(["SALE-A", "SALE-Z"], result.CustomerAlerts.Select(alert => alert.Reference));
        Assert.Equal(["PURCHASE-A", "PURCHASE-Z"], result.SupplierAlerts.Select(alert => alert.Reference));
    }

    [Fact]
    public async Task Sold_and_partial_sales_and_purchases_have_expected_alerts()
    {
        using var database = new AlertTestDatabase();
        var user = database.AddUser("alert-partial", UserRole.Responsable);
        var client = database.AddClient("Partial Alert Client");
        var supplier = database.AddSupplier("Partial Alert Supplier");
        await database.AuthenticateAsync(user);
        var soldSale = database.AddSale(user, client, "SALE-SOLD", CurrentDate.AddDays(1), 1_000);
        var partialSale = database.AddSale(user, client, "SALE-PARTIAL", CurrentDate.AddDays(1), 1_000);
        var soldPurchase = database.AddPurchase(user, supplier, "PURCHASE-SOLD", CurrentDate.AddDays(1), 1_000);
        var partialPurchase = database.AddPurchase(user, supplier, "PURCHASE-PARTIAL", CurrentDate.AddDays(1), 1_000);
        database.AddCustomerPayment(user, client, CurrentDate, 1_000, saleId: soldSale.Id);
        database.AddCustomerPayment(user, client, CurrentDate, 600, saleId: partialSale.Id);
        database.AddSupplierPayment(user, supplier, CurrentDate, 1_000, purchaseId: soldPurchase.Id);
        database.AddSupplierPayment(user, supplier, CurrentDate, 600, purchaseId: partialPurchase.Id);

        var result = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());

        var customerAlert = Assert.Single(result.CustomerAlerts);
        var supplierAlert = Assert.Single(result.SupplierAlerts);
        Assert.Equal("SALE-PARTIAL", customerAlert.Reference);
        Assert.Equal(400, customerAlert.OutstandingMinor);
        Assert.Equal("PURCHASE-PARTIAL", supplierAlert.Reference);
        Assert.Equal(400, supplierAlert.OutstandingMinor);
    }

    [Fact]
    public async Task Initial_obligations_support_partial_payment_then_disappear_when_settled()
    {
        using var database = new AlertTestDatabase();
        var user = database.AddUser("alert-initial", UserRole.Responsable);
        var client = database.AddClient("Initial Alert Client");
        var supplier = database.AddSupplier("Initial Alert Supplier");
        await database.AuthenticateAsync(user);
        var receivable = database.AddInitialReceivable(user, client, "INIT-REC", CurrentDate.AddDays(3), 1_000);
        var payable = database.AddInitialPayable(user, supplier, "INIT-PAY", CurrentDate.AddDays(3), 1_000);
        database.AddCustomerPayment(user, client, CurrentDate, 400, receivableId: receivable.Id);
        database.AddSupplierPayment(user, supplier, CurrentDate, 400, payableId: payable.Id);

        var partial = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());
        database.AddCustomerPayment(user, client, CurrentDate, 600, receivableId: receivable.Id);
        database.AddSupplierPayment(user, supplier, CurrentDate, 600, payableId: payable.Id);
        var settled = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());

        Assert.Equal(600, Assert.Single(partial.CustomerAlerts).OutstandingMinor);
        Assert.Equal(600, Assert.Single(partial.SupplierAlerts).OutstandingMinor);
        Assert.Empty(settled.CustomerAlerts);
        Assert.Empty(settled.SupplierAlerts);
    }

    [Fact]
    public async Task Unallocated_advances_do_not_extinguish_obligations_but_explicit_reallocation_does()
    {
        using var database = new AlertTestDatabase();
        var user = database.AddUser("alert-advance", UserRole.Responsable);
        var client = database.AddClient("Advance Alert Client");
        var supplier = database.AddSupplier("Advance Alert Supplier");
        await database.AuthenticateAsync(user);
        var sale = database.AddSale(user, client, "SALE-ADVANCE", CurrentDate.AddDays(1), 200);
        var purchase = database.AddPurchase(user, supplier, "PURCHASE-ADVANCE", CurrentDate.AddDays(1), 200);
        var customerPayment = database.AddCustomerPayment(user, client, CurrentDate, 200);
        var supplierPayment = database.AddSupplierPayment(user, supplier, CurrentDate, 200);

        var before = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());
        var customerAdvance = await database.CustomerAdvanceService.ApplyAsync(
            new ApplyCustomerAdvanceCommand(
                customerPayment.Id,
                [new CustomerAdvanceAllocationInput(sale.Id, null, 200)]));
        var supplierAdvance = await database.SupplierAdvanceService.ApplyAsync(
            new ApplySupplierAdvanceCommand(
                supplierPayment.Id,
                [new SupplierAdvanceAllocationInput(purchase.Id, null, 200)]));
        var after = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());

        Assert.Equal(200, Assert.Single(before.CustomerAlerts).OutstandingMinor);
        Assert.Equal(200, Assert.Single(before.SupplierAlerts).OutstandingMinor);
        Assert.True(customerAdvance.Success);
        Assert.True(supplierAdvance.Success);
        Assert.Empty(after.CustomerAlerts);
        Assert.Empty(after.SupplierAlerts);
    }

    [Fact]
    public async Task Party_names_and_source_types_are_projected_without_anonymous_fallback()
    {
        using var database = new AlertTestDatabase();
        var user = database.AddUser("alert-party", UserRole.Responsable);
        var client = database.AddClient("Named Alert Client");
        var supplier = database.AddSupplier("Named Alert Supplier");
        await database.AuthenticateAsync(user);
        database.AddSale(user, client, "SALE-NAMED", CurrentDate.AddDays(1), 100);
        database.AddInitialReceivable(user, client, "INIT-REC-NAMED", CurrentDate.AddDays(1), 100);
        database.AddPurchase(user, supplier, "PURCHASE-NAMED", CurrentDate.AddDays(1), 100);
        database.AddInitialPayable(user, supplier, "INIT-PAY-NAMED", CurrentDate.AddDays(1), 100);

        var result = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());

        Assert.Equal(2, result.CustomerAlerts.Count);
        Assert.Equal(2, result.SupplierAlerts.Count);
        Assert.All(result.CustomerAlerts, alert =>
        {
            Assert.Equal(client.Id, alert.PartyId);
            Assert.Equal(client.Name, alert.PartyName);
            Assert.Equal(FinancialDueAlertSide.CustomerReceivable, alert.Side);
        });
        Assert.All(result.SupplierAlerts, alert =>
        {
            Assert.Equal(supplier.Id, alert.PartyId);
            Assert.Equal(supplier.Name, alert.PartyName);
            Assert.Equal(FinancialDueAlertSide.SupplierPayable, alert.Side);
        });
        Assert.Contains(result.CustomerAlerts, alert =>
            alert.SourceType == FinancialDueAlertSourceType.Sale);
        Assert.Contains(result.CustomerAlerts, alert =>
            alert.SourceType == FinancialDueAlertSourceType.InitialCustomerReceivable);
        Assert.Contains(result.SupplierAlerts, alert =>
            alert.SourceType == FinancialDueAlertSourceType.Purchase);
        Assert.Contains(result.SupplierAlerts, alert =>
            alert.SourceType == FinancialDueAlertSourceType.InitialSupplierPayable);
    }

    [Fact]
    public async Task Overallocated_obligations_raise_a_technical_exception()
    {
        using var database = new AlertTestDatabase();
        var user = database.AddUser("alert-corruption", UserRole.Responsable);
        var client = database.AddClient("Corrupt Alert Client");
        var supplier = database.AddSupplier("Corrupt Alert Supplier");
        await database.AuthenticateAsync(user);
        var sale = database.AddSale(user, client, "SALE-CORRUPT", CurrentDate.AddDays(1), 100);
        var purchase = database.AddPurchase(user, supplier, "PURCHASE-CORRUPT", CurrentDate.AddDays(1), 100);
        database.AddCustomerPayment(user, client, CurrentDate, 150, saleId: sale.Id, allocationAmountMinor: 150);
        database.AddSupplierPayment(user, supplier, CurrentDate, 150, purchaseId: purchase.Id, allocationAmountMinor: 150);

        var customerException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery()));
        Assert.Contains("overallocated", customerException.Message, StringComparison.Ordinal);

        database.Context.ChangeTracker.Clear();
        database.Context.Database.ExecuteSqlRaw("DELETE FROM CustomerPaymentAllocations;");
        var supplierException = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery()));
        Assert.Contains("overallocated", supplierException.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Read_is_read_only_and_cancellation_is_propagated()
    {
        using var database = new AlertTestDatabase();
        var user = database.AddUser("alert-read-only", UserRole.Responsable);
        var client = database.AddClient("Read Only Alert Client");
        await database.AuthenticateAsync(user);
        database.AddSale(user, client, "SALE-READ-ONLY", CurrentDate.AddDays(1), 100);
        var before = database.CountPersistedRows();

        var result = await database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery());
        Assert.True(result.Success);
        Assert.Equal(before, database.CountPersistedRows());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.Alerts.GetAlertsAsync(new FinancialDueAlertsQuery(), cancellation.Token));
    }

    private sealed class AlertTestDatabase : IDisposable
    {
        private const string Password = "Alert test password 42!";
        private readonly string databasePath;
        private readonly ServiceProvider serviceProvider;
        private readonly IServiceScope scope;
        private int referenceSequence;

        public AlertTestDatabase()
        {
            databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-Alerts-{Guid.NewGuid():N}.db");
            serviceProvider = new ServiceCollection()
                .AddSingleton<TimeProvider>(new FixedTimeProvider(CurrentUtc))
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
            Alerts = scope.ServiceProvider.GetRequiredService<IFinancialDueAlertService>();
            CustomerAdvanceService = scope.ServiceProvider.GetRequiredService<ICustomerAdvanceService>();
            SupplierAdvanceService = scope.ServiceProvider.GetRequiredService<ISupplierAdvanceService>();
        }

        public FinancialManagementDbContext Context { get; }

        public IFinancialDueAlertService Alerts { get; }

        public ICustomerAdvanceService CustomerAdvanceService { get; }

        public ISupplierAdvanceService SupplierAdvanceService { get; }

        private IPasswordHasher PasswordHasher { get; }

        private IAuthenticationService AuthenticationService { get; }

        public User AddUser(string username, UserRole role)
        {
            var user = new User
            {
                FullName = $"Alert Test {username}",
                Username = username,
                PasswordHash = PasswordHasher.HashPassword(Password),
                Role = role,
                IsActive = true,
                CreatedAtUtc = CurrentUtc,
                UpdatedAtUtc = CurrentUtc
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
                CreatedAtUtc = CurrentUtc,
                UpdatedAtUtc = CurrentUtc
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
                CreatedAtUtc = CurrentUtc,
                UpdatedAtUtc = CurrentUtc
            };
            Context.Suppliers.Add(supplier);
            Context.SaveChanges();
            return supplier;
        }

        public Sale AddSale(
            User user,
            Client? client,
            string reference,
            DateOnly? dueDate,
            long amountMinor)
        {
            var sale = new Sale
            {
                Reference = reference,
                SaleDate = CurrentDate,
                TotalAmountMinor = amountMinor,
                ClientId = client?.Id,
                DueDate = dueDate,
                CreatedByUserId = user.Id,
                CreatedAtUtc = CurrentUtc,
                UpdatedAtUtc = CurrentUtc
            };
            Context.Sales.Add(sale);
            Context.SaveChanges();
            return sale;
        }

        public Purchase AddPurchase(
            User user,
            Supplier supplier,
            string reference,
            DateOnly? dueDate,
            long amountMinor)
        {
            var purchase = new Purchase
            {
                Reference = reference,
                PurchaseDate = CurrentDate,
                TotalAmountMinor = amountMinor,
                SupplierId = supplier.Id,
                DueDate = dueDate,
                CreatedByUserId = user.Id,
                CreatedAtUtc = CurrentUtc,
                UpdatedAtUtc = CurrentUtc
            };
            Context.Purchases.Add(purchase);
            Context.SaveChanges();
            return purchase;
        }

        public CustomerPayment AddCustomerPayment(
            User user,
            Client? client,
            DateOnly date,
            long amountMinor,
            long? saleId = null,
            long? receivableId = null,
            long? allocationAmountMinor = null)
        {
            var payment = new CustomerPayment
            {
                Reference = NextReference("ENC"),
                ClientId = client?.Id,
                PaymentDate = date,
                AmountMinor = amountMinor,
                PaymentMethod = PaymentMethod.Cash,
                CreatedByUserId = user.Id,
                CreatedAtUtc = CurrentUtc
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
                    AmountMinor = allocationAmountMinor ?? amountMinor,
                    CreatedAtUtc = CurrentUtc
                });
                Context.SaveChanges();
            }

            return payment;
        }

        public SupplierPayment AddSupplierPayment(
            User user,
            Supplier supplier,
            DateOnly date,
            long amountMinor,
            long? purchaseId = null,
            long? payableId = null,
            long? allocationAmountMinor = null)
        {
            var payment = new SupplierPayment
            {
                Reference = NextReference("DEC"),
                SupplierId = supplier.Id,
                PaymentDate = date,
                AmountMinor = amountMinor,
                PaymentMethod = PaymentMethod.Cash,
                CreatedByUserId = user.Id,
                CreatedAtUtc = CurrentUtc
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
                    AmountMinor = allocationAmountMinor ?? amountMinor,
                    CreatedAtUtc = CurrentUtc
                });
                Context.SaveChanges();
            }

            return payment;
        }

        public InitialCustomerReceivable AddInitialReceivable(
            User user,
            Client client,
            string reference,
            DateOnly dueDate,
            long amountMinor)
        {
            var receivable = new InitialCustomerReceivable
            {
                Reference = reference,
                ClientId = client.Id,
                ReceivableDate = CurrentDate,
                InitialAmountMinor = amountMinor,
                DueDate = dueDate,
                CreatedByUserId = user.Id,
                CreatedAtUtc = CurrentUtc
            };
            Context.InitialCustomerReceivables.Add(receivable);
            Context.SaveChanges();
            return receivable;
        }

        public InitialSupplierPayable AddInitialPayable(
            User user,
            Supplier supplier,
            string reference,
            DateOnly dueDate,
            long amountMinor)
        {
            var payable = new InitialSupplierPayable
            {
                Reference = reference,
                SupplierId = supplier.Id,
                PayableDate = CurrentDate,
                InitialAmountMinor = amountMinor,
                DueDate = dueDate,
                CreatedByUserId = user.Id,
                CreatedAtUtc = CurrentUtc
            };
            Context.InitialSupplierPayables.Add(payable);
            Context.SaveChanges();
            return payable;
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
            Context.Expenses.Count(),
            Context.TreasuryOpeningBalances.Count(),
            Context.TreasuryTransfers.Count()
        ];

        private string NextReference(string prefix) =>
            $"{prefix}-ALERT-{++referenceSequence:D4}";

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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private static readonly TimeZoneInfo TestTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            "FinancialManagement-Alerts-UTC-02",
            TimeSpan.FromHours(2),
            "FinancialManagement Alerts UTC+02",
            "FinancialManagement Alerts UTC+02");

        public override TimeZoneInfo LocalTimeZone => TestTimeZone;

        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
