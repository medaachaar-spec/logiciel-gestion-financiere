using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
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
    public void Initial_migration_creates_expected_schema_and_seed_idempotently()
    {
        using var database = new MigrationTestDatabase();

        Assert.Empty(database.Context.Database.GetAppliedMigrations());

        database.Context.Database.Migrate();

        var appliedMigrations = database.Context.Database.GetAppliedMigrations().ToArray();
        Assert.Single(appliedMigrations);
        Assert.EndsWith("_001_InitialSchema", appliedMigrations[0], StringComparison.Ordinal);

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

        Assert.Single(database.Context.Database.GetAppliedMigrations());
        Assert.Equal(5, database.Context.ExpenseCategories.Count());
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
