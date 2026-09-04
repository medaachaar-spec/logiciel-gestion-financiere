using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Purchases;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class PurchaseServiceTests
{
    private static readonly DateTimeOffset WorkflowUtc =
        new(2026, 2, 3, 23, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly WorkflowLocalDate = new(2026, 2, 4);

    [Fact]
    public async Task Responsable_can_create_purchase_without_payment()
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser("responsable-no-payment", UserRole.Responsable);
        var supplier = database.AddSupplier("Known Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.PurchaseService.CreateAsync(
            CreateCommand(supplier.Id, 1_000));

        Assert.True(result.Success);
        Assert.Equal(CreatePurchaseFailureReason.None, result.FailureReason);
        Assert.NotNull(result.PurchaseId);
        Assert.Null(result.SupplierPaymentId);
        Assert.Equal(1_000, result.TotalAmountMinor);
        Assert.Equal(0, result.AllocatedAmountMinor);
        Assert.Equal(1_000, result.RemainingPayableMinor);
        Assert.Equal(0, result.UnallocatedSupplierAdvanceMinor);
        Assert.Single(database.Context.Purchases);
        Assert.Empty(database.Context.SupplierPayments);
        Assert.Empty(database.Context.SupplierPaymentAllocations);
        Assert.Empty(database.Context.UserPermissions);
    }

    [Fact]
    public async Task Partial_payment_is_allocated_to_new_purchase()
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser("responsable-partial", UserRole.Responsable);
        var supplier = database.AddSupplier("Partial Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.PurchaseService.CreateAsync(
            CreateCommand(supplier.Id, 1_000, 400));

        AssertSuccessfulPayment(database, result, 1_000, 400, 400, 600, 0);
    }

    [Fact]
    public async Task Exact_payment_clears_payable_without_advance()
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser("responsable-exact", UserRole.Responsable);
        var supplier = database.AddSupplier("Exact Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.PurchaseService.CreateAsync(
            CreateCommand(supplier.Id, 1_000, 1_000));

        AssertSuccessfulPayment(database, result, 1_000, 1_000, 1_000, 0, 0);
    }

    [Fact]
    public async Task Overpayment_creates_only_unallocated_supplier_advance()
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser("responsable-overpayment", UserRole.Responsable);
        var supplier = database.AddSupplier("Overpayment Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.PurchaseService.CreateAsync(
            CreateCommand(supplier.Id, 1_000, 1_200));

        AssertSuccessfulPayment(database, result, 1_000, 1_200, 1_000, 0, 200);
        Assert.True(result.AllocatedAmountMinor <= 1_200);
    }

    [Fact]
    public async Task Overpayment_is_not_allocated_to_an_older_purchase()
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser("responsable-old-purchase", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier With Old Purchase");
        database.Context.Purchases.Add(new Purchase
        {
            Reference = "ACH-OLD-UNPAID",
            PurchaseDate = new DateOnly(2026, 2, 1),
            TotalAmountMinor = 10_000,
            SupplierId = supplier.Id,
            CreatedByUserId = user.Id,
            CreatedAtUtc = WorkflowUtc.AddDays(-2),
            UpdatedAtUtc = WorkflowUtc.AddDays(-2)
        });
        database.Context.SaveChanges();
        var oldPurchaseId = database.Context.Purchases.Single().Id;
        await database.AuthenticateAsync(user);

        var result = await database.PurchaseService.CreateAsync(
            CreateCommand(supplier.Id, 1_000, 1_200));

        Assert.True(result.Success);
        Assert.Equal(1_000, result.AllocatedAmountMinor);
        Assert.Equal(200, result.UnallocatedSupplierAdvanceMinor);
        var allocation = Assert.Single(database.Context.SupplierPaymentAllocations);
        Assert.Equal(result.PurchaseId, allocation.PurchaseId);
        Assert.NotEqual(oldPurchaseId, allocation.PurchaseId);
        Assert.DoesNotContain(
            database.Context.SupplierPaymentAllocations,
            item => item.PurchaseId == oldPurchaseId);
    }

    [Fact]
    public async Task Overpayment_is_not_allocated_to_an_initial_supplier_payable()
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser("responsable-old-payable", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier With Initial Payable");
        database.Context.InitialSupplierPayables.Add(new InitialSupplierPayable
        {
            Reference = "ISP-OLD-UNPAID",
            SupplierId = supplier.Id,
            PayableDate = new DateOnly(2026, 2, 1),
            InitialAmountMinor = 3_000,
            CreatedByUserId = user.Id,
            CreatedAtUtc = WorkflowUtc.AddDays(-2)
        });
        database.Context.SaveChanges();
        var initialPayableId = database.Context.InitialSupplierPayables.Single().Id;
        await database.AuthenticateAsync(user);

        var result = await database.PurchaseService.CreateAsync(
            CreateCommand(supplier.Id, 1_000, 1_200));

        Assert.True(result.Success);
        var allocation = Assert.Single(database.Context.SupplierPaymentAllocations);
        Assert.Equal(result.PurchaseId, allocation.PurchaseId);
        Assert.Null(allocation.InitialSupplierPayableId);
        Assert.DoesNotContain(
            database.Context.SupplierPaymentAllocations,
            item => item.InitialSupplierPayableId == initialPayableId);
    }

    [Fact]
    public async Task Unknown_supplier_is_rejected_without_writes()
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser("unknown-supplier", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.PurchaseService.CreateAsync(
            CreateCommand(999_999, 1_000));

        AssertFunctionalFailure(database, result, CreatePurchaseFailureReason.SupplierNotFound);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Non_positive_total_is_rejected_without_writes(long totalAmountMinor)
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser($"invalid-total-{totalAmountMinor}", UserRole.Responsable);
        var supplier = database.AddSupplier("Invalid Total Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.PurchaseService.CreateAsync(
            CreateCommand(supplier.Id, totalAmountMinor));

        AssertFunctionalFailure(
            database,
            result,
            CreatePurchaseFailureReason.InvalidTotalAmount);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Non_positive_initial_payment_is_rejected_without_writes(
        long paymentAmountMinor)
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser($"invalid-payment-{paymentAmountMinor}", UserRole.Responsable);
        var supplier = database.AddSupplier("Invalid Payment Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.PurchaseService.CreateAsync(
            CreateCommand(supplier.Id, 1_000, paymentAmountMinor));

        AssertFunctionalFailure(
            database,
            result,
            CreatePurchaseFailureReason.InvalidInitialPaymentAmount);
    }

    [Fact]
    public async Task Undefined_payment_method_is_rejected_without_writes()
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser("invalid-method", UserRole.Responsable);
        var supplier = database.AddSupplier("Invalid Method Supplier");
        await database.AuthenticateAsync(user);
        var command = new CreatePurchaseCommand(
            supplier.Id,
            1_000,
            null,
            null,
            new InitialSupplierPaymentInput(1_000, (PaymentMethod)999, null));

        var result = await database.PurchaseService.CreateAsync(command);

        AssertFunctionalFailure(database, result, CreatePurchaseFailureReason.InvalidPaymentMethod);
    }

    [Fact]
    public async Task Unauthenticated_user_is_rejected_without_writes()
    {
        using var database = new PurchaseServiceTestDatabase();

        var result = await database.PurchaseService.CreateAsync(CreateCommand(999_999, 1_000));

        AssertFunctionalFailure(database, result, CreatePurchaseFailureReason.NotAuthenticated);
    }

    [Fact]
    public async Task Employee_without_purchases_create_permission_is_rejected()
    {
        using var database = new PurchaseServiceTestDatabase();
        var employee = database.AddUser("employee-denied", UserRole.Employe);
        var supplier = database.AddSupplier("Employee Denied Supplier");
        await database.AuthenticateAsync(employee);

        var result = await database.PurchaseService.CreateAsync(
            CreateCommand(supplier.Id, 1_000));

        AssertFunctionalFailure(database, result, CreatePurchaseFailureReason.NotAuthorized);
    }

    [Fact]
    public async Task Employee_with_purchases_create_can_add_initial_payment_without_payment_permission()
    {
        using var database = new PurchaseServiceTestDatabase();
        var employee = database.AddUser("employee-purchases-only", UserRole.Employe);
        var supplier = database.AddSupplier("Employee Purchases Supplier");
        database.GrantPurchasesCreate(employee);
        await database.AuthenticateAsync(employee);

        var result = await database.PurchaseService.CreateAsync(
            CreateCommand(supplier.Id, 1_000, 400));

        Assert.True(result.Success);
        Assert.Equal(400, result.AllocatedAmountMinor);
        Assert.DoesNotContain(
            database.Context.UserPermissions,
            permission => permission.Module == PermissionModule.SupplierPayments);
    }

    [Fact]
    public async Task Current_user_time_references_and_supplier_identity_are_persisted()
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser("metadata-user", UserRole.Responsable);
        var supplier = database.AddSupplier("Metadata Supplier");
        await database.AuthenticateAsync(user);
        var dueDate = new DateOnly(2026, 3, 1);
        var command = new CreatePurchaseCommand(
            supplier.Id,
            1_000,
            "Purchase observation",
            dueDate,
            new InitialSupplierPaymentInput(
                500,
                PaymentMethod.BankTransfer,
                "Payment observation"));

        var result = await database.PurchaseService.CreateAsync(command);

        Assert.True(result.Success);
        Assert.Equal(DeterministicReferenceGenerator.PurchaseReference, result.PurchaseReference);
        Assert.Equal(
            DeterministicReferenceGenerator.SupplierPaymentReference,
            result.SupplierPaymentReference);
        var purchase = database.Context.Purchases.AsNoTracking().Single();
        var payment = database.Context.SupplierPayments.AsNoTracking().Single();
        Assert.Equal(supplier.Id, purchase.SupplierId);
        Assert.Equal(supplier.Id, payment.SupplierId);
        Assert.Equal(user.Id, purchase.CreatedByUserId);
        Assert.Equal(user.Id, payment.CreatedByUserId);
        Assert.Equal(WorkflowLocalDate, purchase.PurchaseDate);
        Assert.Equal(WorkflowLocalDate, payment.PaymentDate);
        Assert.Equal(WorkflowUtc, purchase.CreatedAtUtc);
        Assert.Equal(WorkflowUtc, purchase.UpdatedAtUtc);
        Assert.Equal(WorkflowUtc, payment.CreatedAtUtc);
        Assert.Equal(dueDate, purchase.DueDate);
        Assert.Equal("Purchase observation", purchase.Observation);
        Assert.Equal("Payment observation", payment.Observation);
    }

    [Fact]
    public async Task Database_failure_rolls_back_purchase_payment_and_allocation()
    {
        using var database = new PurchaseServiceTestDatabase();
        var user = database.AddUser("rollback-user", UserRole.Responsable);
        var supplier = database.AddSupplier("Rollback Supplier");
        database.Context.SupplierPayments.Add(new SupplierPayment
        {
            Reference = DeterministicReferenceGenerator.SupplierPaymentReference,
            SupplierId = supplier.Id,
            PaymentDate = WorkflowLocalDate,
            AmountMinor = 100,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = user.Id,
            CreatedAtUtc = WorkflowUtc
        });
        database.Context.SaveChanges();
        await database.AuthenticateAsync(user);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            database.PurchaseService.CreateAsync(CreateCommand(supplier.Id, 1_000, 400)));

        database.Context.ChangeTracker.Clear();
        Assert.Empty(database.Context.Purchases);
        Assert.Single(database.Context.SupplierPayments);
        Assert.Empty(database.Context.SupplierPaymentAllocations);
    }

    [Fact]
    public void Default_reference_generator_uses_expected_purchase_formats()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInfrastructure("Data Source=:memory:")
            .BuildServiceProvider();
        var generator = serviceProvider.GetRequiredService<IBusinessReferenceGenerator>();

        var purchaseReference = generator.GeneratePurchaseReference(WorkflowLocalDate);
        var paymentReference = generator.GenerateSupplierPaymentReference(WorkflowLocalDate);

        Assert.Matches("^ACH-20260204-[0-9A-F]{12}$", purchaseReference);
        Assert.Matches("^DEC-20260204-[0-9A-F]{12}$", paymentReference);
    }

    private static CreatePurchaseCommand CreateCommand(
        long supplierId,
        long totalAmountMinor,
        long? paymentAmountMinor = null) =>
        new(
            supplierId,
            totalAmountMinor,
            null,
            null,
            paymentAmountMinor is null
                ? null
                : new InitialSupplierPaymentInput(
                    paymentAmountMinor.Value,
                    PaymentMethod.Cash,
                    null));

    private static void AssertSuccessfulPayment(
        PurchaseServiceTestDatabase database,
        CreatePurchaseResult result,
        long totalAmountMinor,
        long paymentAmountMinor,
        long allocatedAmountMinor,
        long remainingPayableMinor,
        long unallocatedSupplierAdvanceMinor)
    {
        Assert.True(result.Success);
        Assert.Equal(CreatePurchaseFailureReason.None, result.FailureReason);
        Assert.NotNull(result.PurchaseId);
        Assert.NotNull(result.SupplierPaymentId);
        Assert.Equal(totalAmountMinor, result.TotalAmountMinor);
        Assert.Equal(allocatedAmountMinor, result.AllocatedAmountMinor);
        Assert.Equal(remainingPayableMinor, result.RemainingPayableMinor);
        Assert.Equal(unallocatedSupplierAdvanceMinor, result.UnallocatedSupplierAdvanceMinor);
        var purchase = database.Context.Purchases.Single();
        var payment = database.Context.SupplierPayments.Single();
        var allocation = database.Context.SupplierPaymentAllocations.Single();
        Assert.Equal(purchase.SupplierId, payment.SupplierId);
        Assert.Equal(paymentAmountMinor, payment.AmountMinor);
        Assert.Equal(payment.Id, allocation.SupplierPaymentId);
        Assert.Equal(result.PurchaseId, allocation.PurchaseId);
        Assert.Null(allocation.InitialSupplierPayableId);
        Assert.Equal(allocatedAmountMinor, allocation.AmountMinor);
        Assert.True(allocation.AmountMinor <= payment.AmountMinor);
    }

    private static void AssertFunctionalFailure(
        PurchaseServiceTestDatabase database,
        CreatePurchaseResult result,
        CreatePurchaseFailureReason expectedFailure)
    {
        Assert.False(result.Success);
        Assert.Equal(expectedFailure, result.FailureReason);
        Assert.Null(result.PurchaseId);
        Assert.Null(result.PurchaseReference);
        Assert.Null(result.SupplierPaymentId);
        Assert.Null(result.SupplierPaymentReference);
        Assert.Empty(database.Context.Purchases);
        Assert.Empty(database.Context.SupplierPayments);
        Assert.Empty(database.Context.SupplierPaymentAllocations);
    }

    private sealed class PurchaseServiceTestDatabase : IDisposable
    {
        private const string Password = "Purchase service test password 42!";

        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _scope;

        public PurchaseServiceTestDatabase()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-Purchases-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(WorkflowUtc));
            services.AddSingleton<IBusinessReferenceGenerator, DeterministicReferenceGenerator>();
            services.AddInfrastructure($"Data Source={_databasePath};Pooling=False");
            _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            _scope = _serviceProvider.CreateScope();
            Context = _scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
            Context.Database.Migrate();
            PasswordHasher = _scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            AuthenticationService = _scope.ServiceProvider
                .GetRequiredService<IAuthenticationService>();
            PurchaseService = _scope.ServiceProvider.GetRequiredService<IPurchaseService>();
        }

        public FinancialManagementDbContext Context { get; }

        public IPurchaseService PurchaseService { get; }

        private IPasswordHasher PasswordHasher { get; }

        private IAuthenticationService AuthenticationService { get; }

        public User AddUser(string username, UserRole role)
        {
            var user = new User
            {
                FullName = $"Purchase Service Test {username}",
                Username = username,
                PasswordHash = PasswordHasher.HashPassword(Password),
                Role = role,
                IsActive = true,
                CreatedAtUtc = WorkflowUtc,
                UpdatedAtUtc = WorkflowUtc
            };
            Context.Users.Add(user);
            Context.SaveChanges();
            return user;
        }

        public Supplier AddSupplier(string name)
        {
            var supplier = new Supplier
            {
                Name = name,
                CreatedAtUtc = WorkflowUtc,
                UpdatedAtUtc = WorkflowUtc
            };
            Context.Suppliers.Add(supplier);
            Context.SaveChanges();
            return supplier;
        }

        public void GrantPurchasesCreate(User employee)
        {
            Context.UserPermissions.Add(new UserPermission
            {
                UserId = employee.Id,
                Module = PermissionModule.Purchases,
                CanView = true,
                CanCreate = true,
                CanModify = false
            });
            Context.SaveChanges();
        }

        public async Task AuthenticateAsync(User user)
        {
            var result = await AuthenticationService.AuthenticateAsync(user.Username, Password);
            Assert.True(result.Success);
        }

        public void Dispose()
        {
            Context.Database.CloseConnection();
            _scope.Dispose();
            _serviceProvider.Dispose();

            foreach (var path in new[]
            {
                _databasePath,
                $"{_databasePath}-wal",
                $"{_databasePath}-shm",
                $"{_databasePath}-journal"
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
            "FinancialManagement-Test-UTC-02",
            TimeSpan.FromHours(2),
            "FinancialManagement Test UTC+02",
            "FinancialManagement Test UTC+02");

        public override TimeZoneInfo LocalTimeZone => TestTimeZone;

        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class DeterministicReferenceGenerator : IBusinessReferenceGenerator
    {
        public const string PurchaseReference = "ACH-20260204-ABCDEF123456";
        public const string SupplierPaymentReference = "DEC-20260204-ABCDEF123456";

        public string GenerateSaleReference(DateOnly businessDate) =>
            "VTE-20260204-ABCDEF123456";

        public string GenerateCustomerPaymentReference(DateOnly businessDate) =>
            "ENC-20260204-ABCDEF123456";

        public string GeneratePurchaseReference(DateOnly businessDate) => PurchaseReference;

        public string GenerateSupplierPaymentReference(DateOnly businessDate) =>
            SupplierPaymentReference;
    }
}
