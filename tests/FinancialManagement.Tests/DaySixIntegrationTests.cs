using FinancialManagement.Application.Alerts;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Backup;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using FinancialManagement.Infrastructure.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class DaySixIntegrationTests
{
    private const string Password = "Day six integration password 42!";
    private static readonly DateTimeOffset CurrentUtc = new(2026, 9, 4, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly CurrentDate = new(2026, 9, 4);

    [Fact]
    public void Infrastructure_resolves_day_six_services_with_expected_lifetimes()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using var provider = CreateProvider(root);
            using var firstScope = provider.CreateScope();
            using var secondScope = provider.CreateScope();

            Assert.Same(
                firstScope.ServiceProvider.GetRequiredService<ITreasuryService>(),
                firstScope.ServiceProvider.GetRequiredService<ITreasuryService>());
            Assert.Same(
                firstScope.ServiceProvider.GetRequiredService<IFinancialDashboardService>(),
                firstScope.ServiceProvider.GetRequiredService<IFinancialDashboardService>());
            Assert.Same(
                firstScope.ServiceProvider.GetRequiredService<IFinancialDueAlertService>(),
                firstScope.ServiceProvider.GetRequiredService<IFinancialDueAlertService>());
            Assert.Same(
                firstScope.ServiceProvider.GetRequiredService<IDatabaseBackupService>(),
                firstScope.ServiceProvider.GetRequiredService<IDatabaseBackupService>());

            Assert.NotSame(
                firstScope.ServiceProvider.GetRequiredService<ITreasuryService>(),
                secondScope.ServiceProvider.GetRequiredService<ITreasuryService>());
            Assert.NotSame(
                firstScope.ServiceProvider.GetRequiredService<IFinancialDashboardService>(),
                secondScope.ServiceProvider.GetRequiredService<IFinancialDashboardService>());
            Assert.NotSame(
                firstScope.ServiceProvider.GetRequiredService<IFinancialDueAlertService>(),
                secondScope.ServiceProvider.GetRequiredService<IFinancialDueAlertService>());
            Assert.NotSame(
                firstScope.ServiceProvider.GetRequiredService<IDatabaseBackupService>(),
                secondScope.ServiceProvider.GetRequiredService<IDatabaseBackupService>());
            Assert.NotSame(
                firstScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>(),
                secondScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>());

            Assert.Same(
                firstScope.ServiceProvider.GetRequiredService<DatabaseMaintenanceLock>(),
                secondScope.ServiceProvider.GetRequiredService<DatabaseMaintenanceLock>());
            Assert.Same(
                firstScope.ServiceProvider.GetRequiredService<ICurrentUserSession>(),
                secondScope.ServiceProvider.GetRequiredService<ICurrentUserSession>());
            Assert.Same(
                firstScope.ServiceProvider.GetRequiredService<TimeProvider>(),
                secondScope.ServiceProvider.GetRequiredService<TimeProvider>());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Restore_rehydrates_all_day_six_read_models_only_in_a_new_authenticated_scope()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await using var provider = CreateProvider(root);
            var initialScope = provider.CreateAsyncScope();
            string backupPath;
            string preRestorePath;

            try
            {
                var services = initialScope.ServiceProvider;
                var context = services.GetRequiredService<FinancialManagementDbContext>();
                await context.Database.MigrateAsync();
                await SeedStateAAsync(context, services.GetRequiredService<IPasswordHasher>());
                await AuthenticateAsync(services);

                await AssertStateAAsync(services);
                var backup = await services.GetRequiredService<IDatabaseBackupService>()
                    .CreateLocalBackupAsync();
                Assert.True(backup.Success);
                backupPath = backup.BackupPath!;

                context.Expenses.Add(new Expense
                {
                    Reference = "EXP-STATE-B",
                    ExpenseDate = CurrentDate,
                    ExpenseCategoryId = 1,
                    AmountMinor = 999,
                    PaymentMethod = PaymentMethod.Cash,
                    CreatedByUserId = 1,
                    CreatedAtUtc = CurrentUtc,
                    UpdatedAtUtc = CurrentUtc
                });
                await context.SaveChangesAsync();

                var restore = await services.GetRequiredService<IDatabaseBackupService>()
                    .RestoreAsync(new RestoreDatabaseBackupCommand(backupPath));
                Assert.True(restore.Success);
                Assert.True(restore.RestartRequired);
                Assert.False(services.GetRequiredService<ICurrentUserSession>().IsAuthenticated);
                preRestorePath = restore.PreRestoreBackupPath!;
            }
            finally
            {
                await initialScope.DisposeAsync();
            }

            await using var restoredScope = provider.CreateAsyncScope();
            var restoredServices = restoredScope.ServiceProvider;

            var unauthenticatedTreasury = await restoredServices
                .GetRequiredService<ITreasuryService>()
                .GetSummaryAsync();
            var unauthenticatedDashboard = await restoredServices
                .GetRequiredService<IFinancialDashboardService>()
                .GetAsync(new FinancialDashboardQuery(null, null));
            var unauthenticatedAlerts = await restoredServices
                .GetRequiredService<IFinancialDueAlertService>()
                .GetAlertsAsync(new FinancialDueAlertsQuery());
            var unauthenticatedBackup = await restoredServices
                .GetRequiredService<IDatabaseBackupService>()
                .CreateLocalBackupAsync();

            Assert.Equal(TreasurySummaryFailureReason.NotAuthenticated, unauthenticatedTreasury.FailureReason);
            Assert.Equal(FinancialDashboardFailureReason.NotAuthenticated, unauthenticatedDashboard.FailureReason);
            Assert.Equal(FinancialDueAlertsFailureReason.NotAuthenticated, unauthenticatedAlerts.FailureReason);
            Assert.Equal(DatabaseBackupFailureReason.NotAuthenticated, unauthenticatedBackup.FailureReason);

            await AuthenticateAsync(restoredServices);
            await AssertStateAAsync(restoredServices);

            var restoredContext = restoredServices.GetRequiredService<FinancialManagementDbContext>();
            Assert.False(await restoredContext.Expenses.AnyAsync(expense => expense.Reference == "EXP-STATE-B"));
            Assert.Single(await restoredContext.TreasuryOpeningBalances.AsNoTracking().ToArrayAsync());
            Assert.Single(await restoredContext.TreasuryTransfers.AsNoTracking().ToArrayAsync());
            Assert.True(File.Exists(backupPath));
            Assert.True(File.Exists(preRestorePath));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Multi_payment_workflows_are_counted_once_across_dashboard_and_treasury()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await using var provider = CreateProvider(root);
            await using var scope = provider.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<FinancialManagementDbContext>();
            await context.Database.MigrateAsync();

            var user = new User
            {
                Id = 1,
                FullName = "Multi Payment Responsable",
                Username = "day-six-owner",
                PasswordHash = services.GetRequiredService<IPasswordHasher>().HashPassword(Password),
                Role = UserRole.Responsable,
                IsActive = true,
                CreatedAtUtc = CurrentUtc,
                UpdatedAtUtc = CurrentUtc
            };
            var client = new Client
            {
                Id = 1,
                Name = "Multi Payment Client",
                CreatedAtUtc = CurrentUtc,
                UpdatedAtUtc = CurrentUtc
            };
            var supplier = new Supplier
            {
                Id = 1,
                Name = "Multi Payment Supplier",
                CreatedAtUtc = CurrentUtc,
                UpdatedAtUtc = CurrentUtc
            };
            context.AddRange(user, client, supplier);
            await context.SaveChangesAsync();

            var sale = NewSale(user.Id, client.Id, 1_000);
            var purchase = NewPurchase(user.Id, supplier.Id, 1_000);
            var customerPayments = new[]
            {
                NewCustomerPayment(1, user.Id, client.Id, 400, PaymentMethod.Cash),
                NewCustomerPayment(2, user.Id, client.Id, 300, PaymentMethod.BankTransfer),
                NewCustomerPayment(3, user.Id, client.Id, 300, PaymentMethod.Cash)
            };
            var supplierPayments = new[]
            {
                NewSupplierPayment(1, user.Id, supplier.Id, 400, PaymentMethod.Cash),
                NewSupplierPayment(2, user.Id, supplier.Id, 600, PaymentMethod.BankTransfer)
            };
            context.AddRange(sale, purchase);
            context.AddRange(customerPayments);
            context.AddRange(supplierPayments);
            await context.SaveChangesAsync();

            context.CustomerPaymentAllocations.AddRange(customerPayments.Select(payment =>
                new CustomerPaymentAllocation
                {
                    CustomerPaymentId = payment.Id,
                    SaleId = sale.Id,
                    AmountMinor = payment.AmountMinor,
                    CreatedAtUtc = CurrentUtc
                }));
            context.SupplierPaymentAllocations.AddRange(supplierPayments.Select(payment =>
                new SupplierPaymentAllocation
                {
                    SupplierPaymentId = payment.Id,
                    PurchaseId = purchase.Id,
                    AmountMinor = payment.AmountMinor,
                    CreatedAtUtc = CurrentUtc
                }));
            await context.SaveChangesAsync();
            await AuthenticateAsync(services);

            var treasury = await services.GetRequiredService<ITreasuryService>().GetSummaryAsync();
            var dashboard = await services.GetRequiredService<IFinancialDashboardService>()
                .GetAsync(new FinancialDashboardQuery(null, null));

            Assert.Equal(700, treasury.CustomerCashInflowsMinor);
            Assert.Equal(300, treasury.CustomerBankInflowsMinor);
            Assert.Equal(400, treasury.SupplierCashOutflowsMinor);
            Assert.Equal(600, treasury.SupplierBankOutflowsMinor);
            Assert.Equal(300, treasury.CashBalanceMinor);
            Assert.Equal(-300, treasury.BankBalanceMinor);
            Assert.Equal(0, treasury.TotalTreasuryMinor);
            Assert.Equal(1_000, dashboard.TotalSalesMinor);
            Assert.Equal(1_000, dashboard.TotalPurchasesMinor);
            Assert.Equal(1_000, dashboard.CustomerCollectionsMinor);
            Assert.Equal(1_000, dashboard.SupplierPaymentsMinor);
            Assert.Equal(0, dashboard.CurrentCustomerReceivablesMinor);
            Assert.Equal(0, dashboard.CurrentSupplierPayablesMinor);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task Treasury_rejects_an_unknown_persisted_account_instead_of_ignoring_it()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await using var provider = CreateProvider(root);
            await using var scope = provider.CreateAsyncScope();
            var services = scope.ServiceProvider;
            var context = services.GetRequiredService<FinancialManagementDbContext>();
            await context.Database.MigrateAsync();
            await SeedResponsibleAsync(context, services.GetRequiredService<IPasswordHasher>());
            await AuthenticateAsync(services);
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO TreasuryTransfers
                    (Reference, TransferDate, FromAccount, ToAccount, AmountMinor, Observation, CreatedByUserId, CreatedAtUtc)
                VALUES
                    ('TRF-INVALID-ACCOUNT', '2026-09-04', 'Unknown', 'Bank', 100, NULL, 1, '2026-09-04 12:00:00+00:00');
                """);

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                services.GetRequiredService<ITreasuryService>().GetSummaryAsync());

            Assert.Contains("invalid treasury account", exception.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(root, recursive: true);
        }
    }

    private static async Task SeedStateAAsync(
        FinancialManagementDbContext context,
        IPasswordHasher passwordHasher)
    {
        var user = await SeedResponsibleAsync(context, passwordHasher);
        var client = new Client
        {
            Id = 1,
            Name = "Day Six Client",
            CreatedAtUtc = CurrentUtc,
            UpdatedAtUtc = CurrentUtc
        };
        var supplier = new Supplier
        {
            Id = 1,
            Name = "Day Six Supplier",
            CreatedAtUtc = CurrentUtc,
            UpdatedAtUtc = CurrentUtc
        };
        context.AddRange(client, supplier);
        await context.SaveChangesAsync();

        var sale = new Sale
        {
            Id = 1,
            Reference = "SALE-STATE-A",
            SaleDate = CurrentDate,
            DueDate = CurrentDate.AddDays(1),
            TotalAmountMinor = 1_000,
            ClientId = client.Id,
            CreatedByUserId = user.Id,
            CreatedAtUtc = CurrentUtc,
            UpdatedAtUtc = CurrentUtc
        };
        var purchase = new Purchase
        {
            Id = 1,
            Reference = "PURCHASE-STATE-A",
            PurchaseDate = CurrentDate,
            DueDate = CurrentDate.AddDays(1),
            TotalAmountMinor = 800,
            SupplierId = supplier.Id,
            CreatedByUserId = user.Id,
            CreatedAtUtc = CurrentUtc,
            UpdatedAtUtc = CurrentUtc
        };
        var customerPayment = new CustomerPayment
        {
            Id = 1,
            Reference = "CUSTOMER-PAYMENT-STATE-A",
            ClientId = client.Id,
            PaymentDate = CurrentDate,
            AmountMinor = 600,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = user.Id,
            CreatedAtUtc = CurrentUtc
        };
        var supplierPayment = new SupplierPayment
        {
            Id = 1,
            Reference = "SUPPLIER-PAYMENT-STATE-A",
            SupplierId = supplier.Id,
            PaymentDate = CurrentDate,
            AmountMinor = 300,
            PaymentMethod = PaymentMethod.BankTransfer,
            CreatedByUserId = user.Id,
            CreatedAtUtc = CurrentUtc
        };
        context.AddRange(
            sale,
            purchase,
            customerPayment,
            supplierPayment,
            new Expense
            {
                Id = 1,
                Reference = "EXPENSE-STATE-A",
                ExpenseDate = CurrentDate,
                ExpenseCategoryId = 1,
                AmountMinor = 100,
                PaymentMethod = PaymentMethod.Cash,
                CreatedByUserId = user.Id,
                CreatedAtUtc = CurrentUtc,
                UpdatedAtUtc = CurrentUtc
            },
            new TreasuryOpeningBalance
            {
                Id = 1,
                OpeningDate = CurrentDate,
                CashAmountMinor = 1_000,
                BankAmountMinor = 2_000,
                CreatedByUserId = user.Id,
                CreatedAtUtc = CurrentUtc
            },
            new TreasuryTransfer
            {
                Id = 1,
                Reference = "TRF-STATE-A",
                TransferDate = CurrentDate,
                FromAccount = TreasuryAccount.Cash,
                ToAccount = TreasuryAccount.Bank,
                AmountMinor = 200,
                CreatedByUserId = user.Id,
                CreatedAtUtc = CurrentUtc
            });
        await context.SaveChangesAsync();

        context.AddRange(
            new CustomerPaymentAllocation
            {
                CustomerPaymentId = customerPayment.Id,
                SaleId = sale.Id,
                AmountMinor = 600,
                CreatedAtUtc = CurrentUtc
            },
            new SupplierPaymentAllocation
            {
                SupplierPaymentId = supplierPayment.Id,
                PurchaseId = purchase.Id,
                AmountMinor = 300,
                CreatedAtUtc = CurrentUtc
            });
        await context.SaveChangesAsync();
    }

    private static async Task<User> SeedResponsibleAsync(
        FinancialManagementDbContext context,
        IPasswordHasher passwordHasher)
    {
        var user = new User
        {
            Id = 1,
            FullName = "Day Six Responsable",
            Username = "day-six-owner",
            PasswordHash = passwordHasher.HashPassword(Password),
            Role = UserRole.Responsable,
            IsActive = true,
            CreatedAtUtc = CurrentUtc,
            UpdatedAtUtc = CurrentUtc
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static Sale NewSale(long userId, long clientId, long amountMinor) => new()
    {
        Id = 1,
        Reference = "SALE-MULTI-PAYMENT",
        SaleDate = CurrentDate,
        DueDate = CurrentDate.AddDays(1),
        TotalAmountMinor = amountMinor,
        ClientId = clientId,
        CreatedByUserId = userId,
        CreatedAtUtc = CurrentUtc,
        UpdatedAtUtc = CurrentUtc
    };

    private static Purchase NewPurchase(long userId, long supplierId, long amountMinor) => new()
    {
        Id = 1,
        Reference = "PURCHASE-MULTI-PAYMENT",
        PurchaseDate = CurrentDate,
        DueDate = CurrentDate.AddDays(1),
        TotalAmountMinor = amountMinor,
        SupplierId = supplierId,
        CreatedByUserId = userId,
        CreatedAtUtc = CurrentUtc,
        UpdatedAtUtc = CurrentUtc
    };

    private static CustomerPayment NewCustomerPayment(
        long id,
        long userId,
        long clientId,
        long amountMinor,
        PaymentMethod paymentMethod) => new()
    {
        Id = id,
        Reference = $"CUSTOMER-PAYMENT-{id}",
        ClientId = clientId,
        PaymentDate = CurrentDate,
        AmountMinor = amountMinor,
        PaymentMethod = paymentMethod,
        CreatedByUserId = userId,
        CreatedAtUtc = CurrentUtc
    };

    private static SupplierPayment NewSupplierPayment(
        long id,
        long userId,
        long supplierId,
        long amountMinor,
        PaymentMethod paymentMethod) => new()
    {
        Id = id,
        Reference = $"SUPPLIER-PAYMENT-{id}",
        SupplierId = supplierId,
        PaymentDate = CurrentDate,
        AmountMinor = amountMinor,
        PaymentMethod = paymentMethod,
        CreatedByUserId = userId,
        CreatedAtUtc = CurrentUtc
    };

    private static async Task AuthenticateAsync(IServiceProvider services)
    {
        var authentication = await services.GetRequiredService<IAuthenticationService>()
            .AuthenticateAsync("day-six-owner", Password);
        Assert.True(authentication.Success);
    }

    private static async Task AssertStateAAsync(IServiceProvider services)
    {
        var treasury = await services.GetRequiredService<ITreasuryService>().GetSummaryAsync();
        var dashboard = await services.GetRequiredService<IFinancialDashboardService>()
            .GetAsync(new FinancialDashboardQuery(null, null));
        var alerts = await services.GetRequiredService<IFinancialDueAlertService>()
            .GetAlertsAsync(new FinancialDueAlertsQuery());

        Assert.True(treasury.Success);
        Assert.Equal(1_300, treasury.CashBalanceMinor);
        Assert.Equal(1_900, treasury.BankBalanceMinor);
        Assert.Equal(3_200, treasury.TotalTreasuryMinor);

        Assert.True(dashboard.Success);
        Assert.Equal(1_000, dashboard.TotalSalesMinor);
        Assert.Equal(800, dashboard.TotalPurchasesMinor);
        Assert.Equal(600, dashboard.CustomerCollectionsMinor);
        Assert.Equal(300, dashboard.SupplierPaymentsMinor);
        Assert.Equal(100, dashboard.ExpensesMinor);
        Assert.Equal(100, dashboard.ResultMinor);
        Assert.Equal(400, dashboard.CurrentCustomerReceivablesMinor);
        Assert.Equal(500, dashboard.CurrentSupplierPayablesMinor);
        Assert.Equal(1_300, dashboard.CurrentCashBalanceMinor);
        Assert.Equal(1_900, dashboard.CurrentBankBalanceMinor);
        Assert.Equal(3_200, dashboard.CurrentTotalTreasuryMinor);

        Assert.True(alerts.Success);
        Assert.Equal(400, Assert.Single(alerts.CustomerAlerts).OutstandingMinor);
        Assert.Equal(500, Assert.Single(alerts.SupplierAlerts).OutstandingMinor);
    }

    private static ServiceProvider CreateProvider(string root)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(CurrentUtc));
        services.AddSingleton(new DatabaseBackupOptions(Path.Combine(root, "backups")));
        services.AddInfrastructure($"Data Source={Path.Combine(root, "live.db")};Pooling=False");
        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"FinancialManagement-DaySix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;

        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
    }
}
