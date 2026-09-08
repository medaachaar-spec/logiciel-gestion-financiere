using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class FinancialManagementMigrationTests
{
    private static readonly DateTimeOffset SeedTimestamp =
        new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static readonly string[] ExpectedTables =
    [
        "ApplicationSettings",
        "Clients",
        "CustomerPaymentAllocations",
        "CustomerPayments",
        "ExpenseCategories",
        "Expenses",
        "InitialCustomerReceivables",
        "InitialSupplierPayables",
        "Purchases",
        "Sales",
        "SupplierPaymentAllocations",
        "SupplierPayments",
        "Suppliers",
        "TreasuryOpeningBalance",
        "TreasuryTransfers",
        "UserPermissions",
        "Users"
    ];

    [Fact]
    public void All_migrations_create_expected_schema_and_seed_idempotently()
    {
        using var database = new MigrationTestDatabase();

        Assert.Empty(database.Context.Database.GetAppliedMigrations());

        database.Context.Database.Migrate();

        var appliedMigrations = database.Context.Database.GetAppliedMigrations().ToArray();
        Assert.Equal(4, appliedMigrations.Length);
        Assert.EndsWith("_001_InitialSchema", appliedMigrations[0], StringComparison.Ordinal);
        Assert.EndsWith(
            "_002_AllowAnonymousSalePayments",
            appliedMigrations[1],
            StringComparison.Ordinal);
        Assert.EndsWith(
            "_003_TreasuryOpeningBalanceSingleton",
            appliedMigrations[2],
            StringComparison.Ordinal);
        Assert.EndsWith("_004_EnforceFinancialIntegerStorageClass", appliedMigrations[3], StringComparison.Ordinal);

        var tables = database.Context.Database
            .SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master " +
                "WHERE type = 'table' AND name NOT LIKE 'sqlite_%'")
            .AsEnumerable()
            .ToArray();
        Assert.Contains("__EFMigrationsHistory", tables);
        Assert.Contains("__EFMigrationsLock", tables);
        Assert.Equal(
            ExpectedTables.Order(StringComparer.Ordinal),
            tables
                .Where(table => table is not "__EFMigrationsHistory" and not "__EFMigrationsLock")
                .Order(StringComparer.Ordinal));

        var categories = database.Context.ExpenseCategories
            .OrderBy(category => category.Id)
            .ToArray();
        Assert.Equal([1L, 2L, 3L, 4L, 5L], categories.Select(category => category.Id));
        Assert.Equal(
            ["Salaires", "Transport", "Loyer", "Factures", "Divers"],
            categories.Select(category => category.Name));
        Assert.All(categories, category => Assert.True(category.IsActive));
        Assert.All(categories, category => Assert.Equal(SeedTimestamp, category.CreatedAtUtc));

        database.Context.Database.Migrate();

        Assert.Equal(4, database.Context.Database.GetAppliedMigrations().Count());
        Assert.Equal(5, database.Context.ExpenseCategories.Count());
    }

    [Fact]
    public void Third_migration_enforces_single_treasury_opening_balance_row()
    {
        using var database = new MigrationTestDatabase();
        database.Context.Database.Migrate();
        var user = AddMigrationUser(database.Context, "singleton-opening-user");

        database.Context.TreasuryOpeningBalances.Add(new TreasuryOpeningBalance
        {
            OpeningDate = new DateOnly(2026, 1, 1),
            CashAmountMinor = 1_000,
            BankAmountMinor = 2_000,
            CreatedByUserId = user.Id,
            CreatedAtUtc = SeedTimestamp
        });
        database.Context.SaveChanges();
        database.Context.TreasuryOpeningBalances.Add(new TreasuryOpeningBalance
        {
            OpeningDate = new DateOnly(2026, 1, 2),
            CashAmountMinor = 3_000,
            BankAmountMinor = 4_000,
            CreatedByUserId = user.Id,
            CreatedAtUtc = SeedTimestamp
        });

        var exception = Assert.Throws<DbUpdateException>(() =>
            database.Context.SaveChanges());

        var sqliteException = Assert.IsType<SqliteException>(exception.InnerException);
        Assert.Equal(19, sqliteException.SqliteErrorCode);
        Assert.Contains(
            "UX_TreasuryOpeningBalance_Singleton",
            sqliteException.Message,
            StringComparison.Ordinal);
        database.Context.ChangeTracker.Clear();
        Assert.Single(database.Context.TreasuryOpeningBalances.AsNoTracking());
    }

    [Fact]
    public void Third_migration_refuses_existing_duplicate_opening_rows_without_data_loss()
    {
        using var database = new MigrationTestDatabase();
        var migrations = database.Context.Database.GetMigrations().ToArray();
        var secondMigration = migrations.Single(migration =>
            migration.EndsWith("_002_AllowAnonymousSalePayments", StringComparison.Ordinal));
        var thirdMigration = migrations.Single(migration =>
            migration.EndsWith("_003_TreasuryOpeningBalanceSingleton", StringComparison.Ordinal));
        var migrator = database.Context.GetService<IMigrator>();
        migrator.Migrate(secondMigration);
        var user = AddMigrationUser(database.Context, "duplicate-opening-user");
        database.Context.TreasuryOpeningBalances.AddRange(
            new TreasuryOpeningBalance
            {
                OpeningDate = new DateOnly(2026, 1, 1),
                CashAmountMinor = 1_000,
                BankAmountMinor = 2_000,
                CreatedByUserId = user.Id,
                CreatedAtUtc = SeedTimestamp
            },
            new TreasuryOpeningBalance
            {
                OpeningDate = new DateOnly(2026, 1, 2),
                CashAmountMinor = 3_000,
                BankAmountMinor = 4_000,
                CreatedByUserId = user.Id,
                CreatedAtUtc = SeedTimestamp
            });
        database.Context.SaveChanges();
        database.Context.ChangeTracker.Clear();

        var exception = Assert.Throws<SqliteException>(() =>
            migrator.Migrate(thirdMigration));

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Equal(2, database.Context.TreasuryOpeningBalances.AsNoTracking().Count());
        Assert.DoesNotContain(
            thirdMigration,
            database.Context.Database.GetAppliedMigrations());
        Assert.Equal(2, database.Context.Database.GetAppliedMigrations().Count());
    }

    [Fact]
    public void Downgrade_from_third_migration_drops_only_singleton_index()
    {
        using var database = new MigrationTestDatabase();
        database.Context.Database.Migrate();
        var migrations = database.Context.Database.GetMigrations().ToArray();
        var secondMigration = migrations.Single(migration =>
            migration.EndsWith("_002_AllowAnonymousSalePayments", StringComparison.Ordinal));
        var migrator = database.Context.GetService<IMigrator>();
        var user = AddMigrationUser(database.Context, "opening-downgrade-user");
        var original = new TreasuryOpeningBalance
        {
            OpeningDate = new DateOnly(2026, 1, 1),
            CashAmountMinor = -100,
            BankAmountMinor = 200,
            CreatedByUserId = user.Id,
            CreatedAtUtc = SeedTimestamp
        };
        database.Context.TreasuryOpeningBalances.Add(original);
        database.Context.SaveChanges();
        var originalId = original.Id;
        database.Context.ChangeTracker.Clear();

        migrator.Migrate(secondMigration);

        var preserved = database.Context.TreasuryOpeningBalances
            .AsNoTracking()
            .Single(item => item.Id == originalId);
        Assert.Equal(-100, preserved.CashAmountMinor);
        Assert.Equal(200, preserved.BankAmountMinor);
        database.Context.TreasuryOpeningBalances.Add(new TreasuryOpeningBalance
        {
            OpeningDate = new DateOnly(2026, 1, 2),
            CashAmountMinor = 300,
            BankAmountMinor = 400,
            CreatedByUserId = user.Id,
            CreatedAtUtc = SeedTimestamp
        });
        database.Context.SaveChanges();
        Assert.Equal(2, database.Context.TreasuryOpeningBalances.Count());
        Assert.Equal(2, database.Context.Database.GetAppliedMigrations().Count());
    }

    [Fact]
    public void Second_migration_preserves_existing_payment_and_allows_null_client()
    {
        using var database = new MigrationTestDatabase();
        var migrations = database.Context.Database.GetMigrations().ToArray();
        var initialMigration = migrations.Single(migration =>
            migration.EndsWith("_001_InitialSchema", StringComparison.Ordinal));
        var anonymousPaymentsMigration = migrations.Single(migration =>
            migration.EndsWith("_002_AllowAnonymousSalePayments", StringComparison.Ordinal));
        var migrator = database.Context.GetService<IMigrator>();

        migrator.Migrate(initialMigration);

        var user = new User
        {
            FullName = "Migration Compatibility User",
            Username = "migration-compatibility-user",
            PasswordHash = "test-hash",
            Role = UserRole.Responsable,
            IsActive = true,
            CreatedAtUtc = SeedTimestamp,
            UpdatedAtUtc = SeedTimestamp
        };
        var client = new Client
        {
            Name = "Migration Compatibility Client",
            CreatedAtUtc = SeedTimestamp,
            UpdatedAtUtc = SeedTimestamp
        };
        database.Context.AddRange(user, client);
        database.Context.SaveChanges();
        var existingPayment = new CustomerPayment
        {
            Reference = "PAYMENT-BEFORE-002",
            ClientId = client.Id,
            PaymentDate = new DateOnly(2026, 1, 2),
            AmountMinor = 1_000,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = user.Id,
            CreatedAtUtc = SeedTimestamp
        };
        database.Context.CustomerPayments.Add(existingPayment);
        database.Context.SaveChanges();
        var existingPaymentId = existingPayment.Id;
        var existingClientId = client.Id;
        database.Context.ChangeTracker.Clear();

        migrator.Migrate(anonymousPaymentsMigration);

        var preservedPayment = database.Context.CustomerPayments
            .Single(payment => payment.Id == existingPaymentId);
        Assert.Equal(existingClientId, preservedPayment.ClientId);

        database.Context.CustomerPayments.Add(new CustomerPayment
        {
            Reference = "PAYMENT-AFTER-002-ANONYMOUS",
            ClientId = null,
            PaymentDate = new DateOnly(2026, 1, 3),
            AmountMinor = 1_000,
            PaymentMethod = PaymentMethod.BankTransfer,
            CreatedByUserId = user.Id,
            CreatedAtUtc = SeedTimestamp
        });
        database.Context.SaveChanges();

        Assert.Equal(2, database.Context.CustomerPayments.Count());
        Assert.Contains(
            database.Context.CustomerPayments,
            payment => payment.Reference == "PAYMENT-AFTER-002-ANONYMOUS"
                && payment.ClientId == null);
    }

    [Fact]
    public void Downgrade_to_initial_schema_preserves_payments_when_no_client_id_is_null()
    {
        using var database = new MigrationTestDatabase();
        database.Context.Database.Migrate();
        var initialMigration = database.Context.Database.GetMigrations().Single(migration =>
            migration.EndsWith("_001_InitialSchema", StringComparison.Ordinal));
        var migrator = database.Context.GetService<IMigrator>();
        var user = new User
        {
            FullName = "Safe Downgrade User",
            Username = "safe-downgrade-user",
            PasswordHash = "test-hash",
            Role = UserRole.Responsable,
            IsActive = true,
            CreatedAtUtc = SeedTimestamp,
            UpdatedAtUtc = SeedTimestamp
        };
        var client = new Client
        {
            Name = "Safe Downgrade Client",
            CreatedAtUtc = SeedTimestamp,
            UpdatedAtUtc = SeedTimestamp
        };
        database.Context.AddRange(user, client);
        database.Context.SaveChanges();
        var payment = new CustomerPayment
        {
            Reference = "PAYMENT-SAFE-DOWNGRADE",
            ClientId = client.Id,
            PaymentDate = new DateOnly(2026, 1, 2),
            AmountMinor = 1_000,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = user.Id,
            CreatedAtUtc = SeedTimestamp
        };
        database.Context.CustomerPayments.Add(payment);
        database.Context.SaveChanges();
        var paymentId = payment.Id;
        var clientId = client.Id;
        database.Context.ChangeTracker.Clear();

        migrator.Migrate(initialMigration);

        var appliedMigration = Assert.Single(
            database.Context.Database.GetAppliedMigrations());
        Assert.Equal(initialMigration, appliedMigration);
        var preservedPayment = database.Context.CustomerPayments
            .AsNoTracking()
            .Single(item => item.Id == paymentId);
        Assert.Equal(clientId, preservedPayment.ClientId);
    }

    [Fact]
    public void Downgrade_to_initial_schema_is_refused_without_altering_anonymous_payment()
    {
        using var database = new MigrationTestDatabase();
        database.Context.Database.Migrate();
        var migrations = database.Context.Database.GetMigrations().ToArray();
        var initialMigration = migrations.Single(migration =>
            migration.EndsWith("_001_InitialSchema", StringComparison.Ordinal));
        var anonymousPaymentsMigration = migrations.Single(migration =>
            migration.EndsWith("_002_AllowAnonymousSalePayments", StringComparison.Ordinal));
        var migrator = database.Context.GetService<IMigrator>();
        var user = new User
        {
            FullName = "Blocked Downgrade User",
            Username = "blocked-downgrade-user",
            PasswordHash = "test-hash",
            Role = UserRole.Responsable,
            IsActive = true,
            CreatedAtUtc = SeedTimestamp,
            UpdatedAtUtc = SeedTimestamp
        };
        database.Context.Users.Add(user);
        database.Context.SaveChanges();
        var anonymousPayment = new CustomerPayment
        {
            Reference = "PAYMENT-BLOCKED-DOWNGRADE",
            ClientId = null,
            PaymentDate = new DateOnly(2026, 1, 2),
            AmountMinor = 1_000,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = user.Id,
            CreatedAtUtc = SeedTimestamp
        };
        database.Context.CustomerPayments.Add(anonymousPayment);
        database.Context.SaveChanges();
        var paymentId = anonymousPayment.Id;
        database.Context.ChangeTracker.Clear();

        var exception = Assert.Throws<SqliteException>(() =>
            migrator.Migrate(initialMigration));

        Assert.Equal(19, exception.SqliteErrorCode);
        Assert.Contains(
            "CK_CustomerPayments_ClientId_DowngradeGuard",
            exception.Message,
            StringComparison.Ordinal);
        Assert.True(database.Context.Database.CanConnect());
        Assert.Equal(
            [initialMigration, anonymousPaymentsMigration],
            database.Context.Database.GetAppliedMigrations());
        var preservedPayment = database.Context.CustomerPayments
            .AsNoTracking()
            .Single(item => item.Id == paymentId);
        Assert.Null(preservedPayment.ClientId);
        Assert.DoesNotContain(
            database.Context.CustomerPayments.AsNoTracking(),
            item => item.ClientId == 0);
        Assert.Empty(database.Context.Clients.AsNoTracking());
    }

    [Fact]
    public void Migrated_schema_enforces_positive_amount_constraint()
    {
        using var database = new MigrationTestDatabase();
        database.Context.Database.Migrate();
        var user = new User
        {
            FullName = "Migration Test User",
            Username = "migration-test-user",
            PasswordHash = "test-hash",
            Role = UserRole.Responsable,
            IsActive = true,
            CreatedAtUtc = SeedTimestamp,
            UpdatedAtUtc = SeedTimestamp
        };
        database.Context.Users.Add(user);
        database.Context.SaveChanges();
        database.Context.Sales.Add(new Sale
        {
            Reference = "MIGRATED-SALE-ZERO",
            SaleDate = new DateOnly(2026, 1, 2),
            TotalAmountMinor = 0,
            CreatedByUserId = user.Id,
            CreatedAtUtc = SeedTimestamp,
            UpdatedAtUtc = SeedTimestamp
        });

        Assert.Throws<DbUpdateException>(() => database.Context.SaveChanges());
    }

    private static User AddMigrationUser(
        FinancialManagementDbContext context,
        string username)
    {
        var user = new User
        {
            FullName = "Migration Treasury User",
            Username = username,
            PasswordHash = "test-hash",
            Role = UserRole.Responsable,
            IsActive = true,
            CreatedAtUtc = SeedTimestamp,
            UpdatedAtUtc = SeedTimestamp
        };
        context.Users.Add(user);
        context.SaveChanges();
        return user;
    }

    private sealed class MigrationTestDatabase : IDisposable
    {
        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _scope;

        public MigrationTestDatabase()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-Migration-{Guid.NewGuid():N}.db");
            _serviceProvider = new ServiceCollection()
                .AddInfrastructure($"Data Source={_databasePath};Pooling=False")
                .BuildServiceProvider();
            _scope = _serviceProvider.CreateScope();
            Context = _scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        }

        public FinancialManagementDbContext Context { get; }

        public void Dispose()
        {
            Context.Database.CloseConnection();
            _scope.Dispose();
            _serviceProvider.Dispose();

            foreach (var path in new[]
            {
                _databasePath,
                $"{_databasePath}-wal",
                $"{_databasePath}-shm"
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
