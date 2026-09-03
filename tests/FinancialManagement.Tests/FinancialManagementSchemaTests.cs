using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class FinancialManagementSchemaTests
{
    private static readonly DateTimeOffset Timestamp =
        new(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

    [Fact]
    public void EnsureCreated_creates_the_sqlite_schema()
    {
        using var database = new TestDatabase();

        Assert.True(database.WasCreated);
        Assert.True(database.Context.Database.CanConnect());
    }

    [Fact]
    public void Ef_model_contains_exactly_the_seventeen_domain_entities()
    {
        using var database = new TestDatabase();
        var expectedTypes = new[]
        {
            typeof(ApplicationSettings),
            typeof(Client),
            typeof(CustomerPayment),
            typeof(CustomerPaymentAllocation),
            typeof(Expense),
            typeof(ExpenseCategory),
            typeof(InitialCustomerReceivable),
            typeof(InitialSupplierPayable),
            typeof(Purchase),
            typeof(Sale),
            typeof(Supplier),
            typeof(SupplierPayment),
            typeof(SupplierPaymentAllocation),
            typeof(TreasuryOpeningBalance),
            typeof(TreasuryTransfer),
            typeof(User),
            typeof(UserPermission)
        };

        var actualTypes = database.Context.Model
            .GetEntityTypes()
            .Select(entityType => entityType.ClrType)
            .OrderBy(type => type.FullName, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            expectedTypes.OrderBy(type => type.FullName, StringComparer.Ordinal),
            actualTypes);
    }

    [Fact]
    public void Sqlite_schema_contains_exactly_the_expected_tables()
    {
        using var database = new TestDatabase();
        string[] expectedTables =
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

        var actualTables = database.Context.Database
            .SqlQueryRaw<string>(
                "SELECT name AS Value FROM sqlite_master " +
                "WHERE type = 'table' AND name NOT LIKE 'sqlite_%'")
            .AsEnumerable()
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(expectedTables.Order(StringComparer.Ordinal), actualTables);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Customer_allocation_rejects_zero_or_two_targets(
        bool targetSale,
        bool targetInitialReceivable)
    {
        using var database = new TestDatabase();
        var scenario = CreateCustomerScenario(database.Context);
        var allocation = new CustomerPaymentAllocation
        {
            CustomerPaymentId = scenario.Payment.Id,
            SaleId = targetSale ? scenario.Sale.Id : null,
            InitialCustomerReceivableId = targetInitialReceivable
                ? scenario.Receivable.Id
                : null,
            AmountMinor = 100,
            CreatedAtUtc = Timestamp
        };

        database.Context.CustomerPaymentAllocations.Add(allocation);

        Assert.Throws<DbUpdateException>(() => database.Context.SaveChanges());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Supplier_allocation_rejects_zero_or_two_targets(
        bool targetPurchase,
        bool targetInitialPayable)
    {
        using var database = new TestDatabase();
        var scenario = CreateSupplierScenario(database.Context);
        var allocation = new SupplierPaymentAllocation
        {
            SupplierPaymentId = scenario.Payment.Id,
            PurchaseId = targetPurchase ? scenario.Purchase.Id : null,
            InitialSupplierPayableId = targetInitialPayable ? scenario.Payable.Id : null,
            AmountMinor = 100,
            CreatedAtUtc = Timestamp
        };

        database.Context.SupplierPaymentAllocations.Add(allocation);

        Assert.Throws<DbUpdateException>(() => database.Context.SaveChanges());
    }

    [Fact]
    public void Allocations_with_exactly_one_existing_target_are_accepted()
    {
        using var database = new TestDatabase();
        var customerScenario = CreateCustomerScenario(database.Context);
        var supplierScenario = CreateSupplierScenario(database.Context);

        database.Context.CustomerPaymentAllocations.Add(new CustomerPaymentAllocation
        {
            CustomerPaymentId = customerScenario.Payment.Id,
            SaleId = customerScenario.Sale.Id,
            AmountMinor = 100,
            CreatedAtUtc = Timestamp
        });
        database.Context.SupplierPaymentAllocations.Add(new SupplierPaymentAllocation
        {
            SupplierPaymentId = supplierScenario.Payment.Id,
            PurchaseId = supplierScenario.Purchase.Id,
            AmountMinor = 100,
            CreatedAtUtc = Timestamp
        });

        database.Context.SaveChanges();

        Assert.Single(database.Context.CustomerPaymentAllocations);
        Assert.Single(database.Context.SupplierPaymentAllocations);
    }

    [Fact]
    public void Positive_amount_check_rejects_zero()
    {
        using var database = new TestDatabase();
        var user = CreateUser("zero-amount-user");
        database.Context.Users.Add(user);
        database.Context.SaveChanges();
        database.Context.Sales.Add(new Sale
        {
            Reference = "SALE-ZERO",
            SaleDate = new DateOnly(2026, 1, 2),
            TotalAmountMinor = 0,
            CreatedByUserId = user.Id,
            CreatedAtUtc = Timestamp,
            UpdatedAtUtc = Timestamp
        });

        Assert.Throws<DbUpdateException>(() => database.Context.SaveChanges());
    }

    [Fact]
    public void Treasury_transfer_rejects_identical_accounts()
    {
        using var database = new TestDatabase();
        var user = CreateUser("transfer-user");
        database.Context.Users.Add(user);
        database.Context.SaveChanges();
        database.Context.TreasuryTransfers.Add(new TreasuryTransfer
        {
            Reference = "TRANSFER-INVALID",
            TransferDate = new DateOnly(2026, 1, 2),
            FromAccount = TreasuryAccount.Cash,
            ToAccount = TreasuryAccount.Cash,
            AmountMinor = 100,
            CreatedByUserId = user.Id,
            CreatedAtUtc = Timestamp
        });

        Assert.Throws<DbUpdateException>(() => database.Context.SaveChanges());
    }

    [Fact]
    public void Username_is_unique()
    {
        using var database = new TestDatabase();
        database.Context.Users.Add(CreateUser("unique-user"));
        database.Context.SaveChanges();
        database.Context.Users.Add(CreateUser("unique-user"));

        Assert.Throws<DbUpdateException>(() => database.Context.SaveChanges());
    }

    [Fact]
    public void User_permission_module_is_unique_per_user()
    {
        using var database = new TestDatabase();
        var user = CreateUser("permission-user");
        database.Context.Users.Add(user);
        database.Context.SaveChanges();
        database.Context.UserPermissions.Add(new UserPermission
        {
            UserId = user.Id,
            Module = PermissionModule.Sales,
            CanView = true
        });
        database.Context.SaveChanges();
        database.Context.UserPermissions.Add(new UserPermission
        {
            UserId = user.Id,
            Module = PermissionModule.Sales,
            CanView = true,
            CanCreate = true
        });

        Assert.Throws<DbUpdateException>(() => database.Context.SaveChanges());
    }

    [Fact]
    public void Financial_reference_is_unique()
    {
        using var database = new TestDatabase();
        var user = CreateUser("reference-user");
        database.Context.Users.Add(user);
        database.Context.SaveChanges();
        database.Context.Sales.Add(CreateSale("SALE-UNIQUE", user.Id));
        database.Context.SaveChanges();
        database.Context.Sales.Add(CreateSale("SALE-UNIQUE", user.Id));

        Assert.Throws<DbUpdateException>(() => database.Context.SaveChanges());
    }

    [Fact]
    public void Deleting_a_client_used_by_a_sale_is_restricted()
    {
        using var database = new TestDatabase();
        var user = CreateUser("restrict-user");
        var client = CreateClient();
        database.Context.AddRange(user, client);
        database.Context.SaveChanges();
        var sale = CreateSale("SALE-RESTRICT", user.Id, client.Id);
        database.Context.Sales.Add(sale);
        database.Context.SaveChanges();

        var clientId = client.Id;
        database.Context.ChangeTracker.Clear();
        var persistedClient = database.Context.Clients.Single(item => item.Id == clientId);
        database.Context.Clients.Remove(persistedClient);

        Assert.Throws<DbUpdateException>(() => database.Context.SaveChanges());
    }

    [Fact]
    public void Deleting_a_customer_payment_cascades_to_its_allocations()
    {
        using var database = new TestDatabase();
        var scenario = CreateCustomerScenario(database.Context);
        database.Context.CustomerPaymentAllocations.Add(new CustomerPaymentAllocation
        {
            CustomerPaymentId = scenario.Payment.Id,
            SaleId = scenario.Sale.Id,
            AmountMinor = 100,
            CreatedAtUtc = Timestamp
        });
        database.Context.SaveChanges();
        var paymentId = scenario.Payment.Id;

        database.Context.ChangeTracker.Clear();
        var payment = database.Context.CustomerPayments.Single(item => item.Id == paymentId);
        database.Context.CustomerPayments.Remove(payment);
        database.Context.SaveChanges();

        Assert.DoesNotContain(
            database.Context.CustomerPaymentAllocations,
            allocation => allocation.CustomerPaymentId == paymentId);
    }

    [Fact]
    public void DateOnly_and_DateTimeOffset_values_round_trip_through_sqlite()
    {
        using var database = new TestDatabase();
        var user = CreateUser("date-user");
        var openingDate = new DateOnly(2026, 2, 3);
        database.Context.Users.Add(user);
        database.Context.SaveChanges();
        database.Context.TreasuryOpeningBalances.Add(new TreasuryOpeningBalance
        {
            OpeningDate = openingDate,
            CashAmountMinor = -500,
            BankAmountMinor = 0,
            CreatedByUserId = user.Id,
            CreatedAtUtc = Timestamp
        });
        database.Context.SaveChanges();

        database.Context.ChangeTracker.Clear();
        var balance = database.Context.TreasuryOpeningBalances.Single();

        Assert.Equal(openingDate, balance.OpeningDate);
        Assert.Equal(Timestamp, balance.CreatedAtUtc);
        Assert.Equal(-500, balance.CashAmountMinor);
        Assert.Equal(0, balance.BankAmountMinor);
    }

    private static CustomerScenario CreateCustomerScenario(
        FinancialManagementDbContext context)
    {
        var user = CreateUser($"customer-user-{Guid.NewGuid():N}");
        var client = CreateClient();
        context.AddRange(user, client);
        context.SaveChanges();

        var payment = new CustomerPayment
        {
            Reference = $"CUSTOMER-PAYMENT-{Guid.NewGuid():N}",
            ClientId = client.Id,
            PaymentDate = new DateOnly(2026, 1, 2),
            AmountMinor = 1_000,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = user.Id,
            CreatedAtUtc = Timestamp
        };
        var sale = CreateSale($"SALE-{Guid.NewGuid():N}", user.Id, client.Id);
        var receivable = new InitialCustomerReceivable
        {
            Reference = $"RECEIVABLE-{Guid.NewGuid():N}",
            ClientId = client.Id,
            ReceivableDate = new DateOnly(2026, 1, 1),
            InitialAmountMinor = 1_000,
            CreatedByUserId = user.Id,
            CreatedAtUtc = Timestamp
        };
        context.AddRange(payment, sale, receivable);
        context.SaveChanges();

        return new CustomerScenario(payment, sale, receivable);
    }

    private static SupplierScenario CreateSupplierScenario(
        FinancialManagementDbContext context)
    {
        var user = CreateUser($"supplier-user-{Guid.NewGuid():N}");
        var supplier = CreateSupplier();
        context.AddRange(user, supplier);
        context.SaveChanges();

        var payment = new SupplierPayment
        {
            Reference = $"SUPPLIER-PAYMENT-{Guid.NewGuid():N}",
            SupplierId = supplier.Id,
            PaymentDate = new DateOnly(2026, 1, 2),
            AmountMinor = 1_000,
            PaymentMethod = PaymentMethod.BankTransfer,
            CreatedByUserId = user.Id,
            CreatedAtUtc = Timestamp
        };
        var purchase = new Purchase
        {
            Reference = $"PURCHASE-{Guid.NewGuid():N}",
            PurchaseDate = new DateOnly(2026, 1, 2),
            TotalAmountMinor = 1_000,
            SupplierId = supplier.Id,
            CreatedByUserId = user.Id,
            CreatedAtUtc = Timestamp,
            UpdatedAtUtc = Timestamp
        };
        var payable = new InitialSupplierPayable
        {
            Reference = $"PAYABLE-{Guid.NewGuid():N}",
            SupplierId = supplier.Id,
            PayableDate = new DateOnly(2026, 1, 1),
            InitialAmountMinor = 1_000,
            CreatedByUserId = user.Id,
            CreatedAtUtc = Timestamp
        };
        context.AddRange(payment, purchase, payable);
        context.SaveChanges();

        return new SupplierScenario(payment, purchase, payable);
    }

    private static User CreateUser(string username) => new()
    {
        FullName = "Test User",
        Username = username,
        PasswordHash = "test-hash",
        Role = UserRole.Responsable,
        IsActive = true,
        CreatedAtUtc = Timestamp,
        UpdatedAtUtc = Timestamp
    };

    private static Client CreateClient() => new()
    {
        Name = "Test Client",
        CreatedAtUtc = Timestamp,
        UpdatedAtUtc = Timestamp
    };

    private static Supplier CreateSupplier() => new()
    {
        Name = "Test Supplier",
        CreatedAtUtc = Timestamp,
        UpdatedAtUtc = Timestamp
    };

    private static Sale CreateSale(string reference, long userId, long? clientId = null) => new()
    {
        Reference = reference,
        SaleDate = new DateOnly(2026, 1, 2),
        TotalAmountMinor = 1_000,
        ClientId = clientId,
        CreatedByUserId = userId,
        CreatedAtUtc = Timestamp,
        UpdatedAtUtc = Timestamp
    };

    private sealed record CustomerScenario(
        CustomerPayment Payment,
        Sale Sale,
        InitialCustomerReceivable Receivable);

    private sealed record SupplierScenario(
        SupplierPayment Payment,
        Purchase Purchase,
        InitialSupplierPayable Payable);

    private sealed class TestDatabase : IDisposable
    {
        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _scope;

        public TestDatabase()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-Schema-{Guid.NewGuid():N}.db");
            _serviceProvider = new ServiceCollection()
                .AddInfrastructure($"Data Source={_databasePath};Pooling=False")
                .BuildServiceProvider();
            _scope = _serviceProvider.CreateScope();
            Context = _scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
            WasCreated = Context.Database.EnsureCreated();
        }

        public FinancialManagementDbContext Context { get; }
        public bool WasCreated { get; }

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
