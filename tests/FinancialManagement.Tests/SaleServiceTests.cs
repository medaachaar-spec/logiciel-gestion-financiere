using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class SaleServiceTests
{
    private static readonly DateTimeOffset WorkflowUtc =
        new(2026, 2, 3, 23, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly WorkflowLocalDate = new(2026, 2, 4);

    [Fact]
    public async Task Responsable_can_create_known_client_sale_without_payment()
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser("responsable-no-payment", UserRole.Responsable);
        var client = database.AddClient("Known Client");
        await database.AuthenticateAsync(user);

        var result = await database.SaleService.CreateAsync(
            CreateKnownClientCommand(client.Id, 1_000));

        Assert.True(result.Success);
        Assert.Equal(CreateSaleFailureReason.None, result.FailureReason);
        Assert.NotNull(result.SaleId);
        Assert.Null(result.CustomerPaymentId);
        Assert.Equal(1_000, result.TotalAmountMinor);
        Assert.Equal(0, result.AllocatedAmountMinor);
        Assert.Equal(1_000, result.RemainingReceivableMinor);
        Assert.Equal(0, result.UnallocatedAdvanceMinor);
        Assert.Single(database.Context.Sales);
        Assert.Empty(database.Context.CustomerPayments);
        Assert.Empty(database.Context.CustomerPaymentAllocations);
        Assert.Empty(database.Context.UserPermissions);
    }

    [Fact]
    public async Task Known_client_partial_payment_is_fully_allocated_to_new_sale()
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser("responsable-partial", UserRole.Responsable);
        var client = database.AddClient("Partial Client");
        await database.AuthenticateAsync(user);

        var result = await database.SaleService.CreateAsync(
            CreateKnownClientCommand(client.Id, 1_000, 400));

        AssertSuccessfulPayment(database, result, 1_000, 400, 400, 600, 0);
    }

    [Fact]
    public async Task Known_client_exact_payment_clears_receivable_without_advance()
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser("responsable-exact", UserRole.Responsable);
        var client = database.AddClient("Exact Client");
        await database.AuthenticateAsync(user);

        var result = await database.SaleService.CreateAsync(
            CreateKnownClientCommand(client.Id, 1_000, 1_000));

        AssertSuccessfulPayment(database, result, 1_000, 1_000, 1_000, 0, 0);
    }

    [Fact]
    public async Task Known_client_overpayment_creates_only_unallocated_advance()
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser("responsable-overpayment", UserRole.Responsable);
        var client = database.AddClient("Overpayment Client");
        await database.AuthenticateAsync(user);

        var result = await database.SaleService.CreateAsync(
            CreateKnownClientCommand(client.Id, 1_000, 1_250));

        AssertSuccessfulPayment(database, result, 1_000, 1_250, 1_000, 0, 250);
        Assert.True(result.AllocatedAmountMinor <= 1_250);
    }

    [Fact]
    public async Task Overpayment_is_not_allocated_to_an_older_sale()
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser("responsable-old-sale", UserRole.Responsable);
        var client = database.AddClient("Client With Old Sale");
        database.Context.Sales.Add(new Sale
        {
            Reference = "VTE-OLD-UNPAID",
            SaleDate = new DateOnly(2026, 2, 1),
            TotalAmountMinor = 10_000,
            ClientId = client.Id,
            CreatedByUserId = user.Id,
            CreatedAtUtc = WorkflowUtc.AddDays(-2),
            UpdatedAtUtc = WorkflowUtc.AddDays(-2)
        });
        database.Context.SaveChanges();
        var oldSaleId = database.Context.Sales.Single().Id;
        await database.AuthenticateAsync(user);

        var result = await database.SaleService.CreateAsync(
            CreateKnownClientCommand(client.Id, 20_000, 25_000));

        Assert.True(result.Success);
        Assert.Equal(20_000, result.AllocatedAmountMinor);
        Assert.Equal(5_000, result.UnallocatedAdvanceMinor);
        var allocation = Assert.Single(database.Context.CustomerPaymentAllocations);
        Assert.Equal(result.SaleId, allocation.SaleId);
        Assert.NotEqual(oldSaleId, allocation.SaleId);
        Assert.DoesNotContain(
            database.Context.CustomerPaymentAllocations,
            item => item.SaleId == oldSaleId);
    }

    [Fact]
    public async Task Anonymous_sale_with_exact_payment_is_created_without_client_or_balance()
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser("responsable-anonymous", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.SaleService.CreateAsync(
            CreateAnonymousCommand(1_000, 1_000));

        AssertSuccessfulPayment(database, result, 1_000, 1_000, 1_000, 0, 0);
        Assert.Null(database.Context.Sales.Single().ClientId);
        Assert.Null(database.Context.CustomerPayments.Single().ClientId);
        Assert.Null(database.Context.CustomerPaymentAllocations
            .Single()
            .InitialCustomerReceivableId);
        Assert.Empty(database.Context.Clients);
    }

    [Theory]
    [InlineData(null, CreateSaleFailureReason.AnonymousSaleRequiresInitialPayment)]
    [InlineData(500L, CreateSaleFailureReason.AnonymousSalePaymentMustMatchTotal)]
    [InlineData(1_500L, CreateSaleFailureReason.AnonymousSalePaymentMustMatchTotal)]
    public async Task Anonymous_sale_requires_exact_payment(
        long? paymentAmountMinor,
        CreateSaleFailureReason expectedFailure)
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser($"anonymous-invalid-{paymentAmountMinor}", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        var command = paymentAmountMinor is null
            ? new CreateSaleCommand(null, 1_000, null, null, null)
            : CreateAnonymousCommand(1_000, paymentAmountMinor.Value);

        var result = await database.SaleService.CreateAsync(command);

        AssertFunctionalFailure(database, result, expectedFailure);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Non_positive_sale_total_is_rejected_without_writes(long totalAmountMinor)
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser($"invalid-total-{totalAmountMinor}", UserRole.Responsable);
        var client = database.AddClient("Invalid Total Client");
        await database.AuthenticateAsync(user);

        var result = await database.SaleService.CreateAsync(
            CreateKnownClientCommand(client.Id, totalAmountMinor));

        AssertFunctionalFailure(
            database,
            result,
            CreateSaleFailureReason.InvalidTotalAmount);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Non_positive_initial_payment_is_rejected_without_writes(
        long paymentAmountMinor)
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser($"invalid-payment-{paymentAmountMinor}", UserRole.Responsable);
        var client = database.AddClient("Invalid Payment Client");
        await database.AuthenticateAsync(user);

        var result = await database.SaleService.CreateAsync(
            CreateKnownClientCommand(client.Id, 1_000, paymentAmountMinor));

        AssertFunctionalFailure(
            database,
            result,
            CreateSaleFailureReason.InvalidInitialPaymentAmount);
    }

    [Fact]
    public async Task Unknown_client_is_rejected_without_writes()
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser("unknown-client", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.SaleService.CreateAsync(
            CreateKnownClientCommand(999_999, 1_000));

        AssertFunctionalFailure(database, result, CreateSaleFailureReason.ClientNotFound);
    }

    [Fact]
    public async Task Unauthenticated_user_is_rejected_without_writes()
    {
        using var database = new SaleServiceTestDatabase();

        var result = await database.SaleService.CreateAsync(
            CreateKnownClientCommand(999_999, 1_000));

        AssertFunctionalFailure(database, result, CreateSaleFailureReason.NotAuthenticated);
    }

    [Fact]
    public async Task Employee_without_sales_create_permission_is_rejected()
    {
        using var database = new SaleServiceTestDatabase();
        var employee = database.AddUser("employee-denied", UserRole.Employe);
        var client = database.AddClient("Employee Denied Client");
        await database.AuthenticateAsync(employee);

        var result = await database.SaleService.CreateAsync(
            CreateKnownClientCommand(client.Id, 1_000));

        AssertFunctionalFailure(database, result, CreateSaleFailureReason.NotAuthorized);
    }

    [Fact]
    public async Task Employee_with_sales_create_can_add_initial_payment_without_payment_permission()
    {
        using var database = new SaleServiceTestDatabase();
        var employee = database.AddUser("employee-sales-only", UserRole.Employe);
        var client = database.AddClient("Employee Sales Client");
        database.GrantSalesCreate(employee);
        await database.AuthenticateAsync(employee);

        var result = await database.SaleService.CreateAsync(
            CreateKnownClientCommand(client.Id, 1_000, 400));

        Assert.True(result.Success);
        Assert.Equal(400, result.AllocatedAmountMinor);
        Assert.DoesNotContain(
            database.Context.UserPermissions,
            permission => permission.Module == PermissionModule.CustomerPayments);
    }

    [Fact]
    public async Task Undefined_payment_method_is_rejected_without_writes()
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser("invalid-method", UserRole.Responsable);
        var client = database.AddClient("Invalid Method Client");
        await database.AuthenticateAsync(user);
        var command = new CreateSaleCommand(
            client.Id,
            1_000,
            null,
            null,
            new InitialCustomerPaymentInput(1_000, (PaymentMethod)999, null));

        var result = await database.SaleService.CreateAsync(command);

        AssertFunctionalFailure(database, result, CreateSaleFailureReason.InvalidPaymentMethod);
    }

    [Fact]
    public async Task Current_user_time_and_injected_references_are_persisted()
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser("metadata-user", UserRole.Responsable);
        var client = database.AddClient("Metadata Client");
        await database.AuthenticateAsync(user);
        var dueDate = new DateOnly(2026, 3, 1);
        var command = new CreateSaleCommand(
            client.Id,
            1_000,
            "Sale observation",
            dueDate,
            new InitialCustomerPaymentInput(
                500,
                PaymentMethod.BankTransfer,
                "Payment observation"));

        var result = await database.SaleService.CreateAsync(command);

        Assert.True(result.Success);
        Assert.Equal(DeterministicReferenceGenerator.SaleReference, result.SaleReference);
        Assert.Equal(
            DeterministicReferenceGenerator.CustomerPaymentReference,
            result.CustomerPaymentReference);
        var sale = database.Context.Sales.AsNoTracking().Single();
        var payment = database.Context.CustomerPayments.AsNoTracking().Single();
        Assert.Equal(user.Id, sale.CreatedByUserId);
        Assert.Equal(user.Id, payment.CreatedByUserId);
        Assert.Equal(WorkflowLocalDate, sale.SaleDate);
        Assert.Equal(WorkflowLocalDate, payment.PaymentDate);
        Assert.Equal(WorkflowUtc, sale.CreatedAtUtc);
        Assert.Equal(WorkflowUtc, sale.UpdatedAtUtc);
        Assert.Equal(WorkflowUtc, payment.CreatedAtUtc);
        Assert.Equal(dueDate, sale.DueDate);
        Assert.Equal("Sale observation", sale.Observation);
        Assert.Equal("Payment observation", payment.Observation);
        Assert.Equal(DeterministicReferenceGenerator.SaleReference, sale.Reference);
        Assert.Equal(
            DeterministicReferenceGenerator.CustomerPaymentReference,
            payment.Reference);
    }

    [Fact]
    public async Task Database_failure_rolls_back_sale_payment_and_allocation()
    {
        using var database = new SaleServiceTestDatabase();
        var user = database.AddUser("rollback-user", UserRole.Responsable);
        var client = database.AddClient("Rollback Client");
        database.Context.CustomerPayments.Add(new CustomerPayment
        {
            Reference = DeterministicReferenceGenerator.CustomerPaymentReference,
            ClientId = client.Id,
            PaymentDate = WorkflowLocalDate,
            AmountMinor = 100,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = user.Id,
            CreatedAtUtc = WorkflowUtc
        });
        database.Context.SaveChanges();
        await database.AuthenticateAsync(user);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            database.SaleService.CreateAsync(
                CreateKnownClientCommand(client.Id, 1_000, 400)));

        database.Context.ChangeTracker.Clear();
        Assert.Empty(database.Context.Sales);
        Assert.Single(database.Context.CustomerPayments);
        Assert.Empty(database.Context.CustomerPaymentAllocations);
    }

    [Fact]
    public void Default_reference_generator_uses_expected_formats()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInfrastructure("Data Source=:memory:")
            .BuildServiceProvider();
        var generator = serviceProvider.GetRequiredService<IBusinessReferenceGenerator>();

        var saleReference = generator.GenerateSaleReference(WorkflowLocalDate);
        var paymentReference = generator.GenerateCustomerPaymentReference(WorkflowLocalDate);

        Assert.Matches("^VTE-20260204-[0-9A-F]{12}$", saleReference);
        Assert.Matches("^ENC-20260204-[0-9A-F]{12}$", paymentReference);
    }

    private static CreateSaleCommand CreateKnownClientCommand(
        long clientId,
        long totalAmountMinor,
        long? paymentAmountMinor = null) =>
        new(
            clientId,
            totalAmountMinor,
            null,
            null,
            paymentAmountMinor is null
                ? null
                : new InitialCustomerPaymentInput(
                    paymentAmountMinor.Value,
                    PaymentMethod.Cash,
                    null));

    private static CreateSaleCommand CreateAnonymousCommand(
        long totalAmountMinor,
        long paymentAmountMinor) =>
        new(
            null,
            totalAmountMinor,
            null,
            null,
            new InitialCustomerPaymentInput(
                paymentAmountMinor,
                PaymentMethod.Cash,
                null));

    private static void AssertSuccessfulPayment(
        SaleServiceTestDatabase database,
        CreateSaleResult result,
        long totalAmountMinor,
        long paymentAmountMinor,
        long allocatedAmountMinor,
        long remainingReceivableMinor,
        long unallocatedAdvanceMinor)
    {
        Assert.True(result.Success);
        Assert.Equal(CreateSaleFailureReason.None, result.FailureReason);
        Assert.NotNull(result.SaleId);
        Assert.NotNull(result.CustomerPaymentId);
        Assert.Equal(totalAmountMinor, result.TotalAmountMinor);
        Assert.Equal(allocatedAmountMinor, result.AllocatedAmountMinor);
        Assert.Equal(remainingReceivableMinor, result.RemainingReceivableMinor);
        Assert.Equal(unallocatedAdvanceMinor, result.UnallocatedAdvanceMinor);
        var sale = database.Context.Sales.Single();
        var payment = database.Context.CustomerPayments.Single();
        var allocation = database.Context.CustomerPaymentAllocations.Single();
        Assert.Equal(sale.ClientId, payment.ClientId);
        Assert.Equal(paymentAmountMinor, payment.AmountMinor);
        Assert.Equal(payment.Id, allocation.CustomerPaymentId);
        Assert.Equal(result.SaleId, allocation.SaleId);
        Assert.Equal(allocatedAmountMinor, allocation.AmountMinor);
        Assert.True(allocation.AmountMinor <= payment.AmountMinor);
    }

    private static void AssertFunctionalFailure(
        SaleServiceTestDatabase database,
        CreateSaleResult result,
        CreateSaleFailureReason expectedFailure)
    {
        Assert.False(result.Success);
        Assert.Equal(expectedFailure, result.FailureReason);
        Assert.Null(result.SaleId);
        Assert.Null(result.SaleReference);
        Assert.Null(result.CustomerPaymentId);
        Assert.Null(result.CustomerPaymentReference);
        Assert.Empty(database.Context.Sales);
        Assert.Empty(database.Context.CustomerPayments);
        Assert.Empty(database.Context.CustomerPaymentAllocations);
    }

    private sealed class SaleServiceTestDatabase : IDisposable
    {
        private const string Password = "Sale service test password 42!";

        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _scope;

        public SaleServiceTestDatabase()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-Sales-{Guid.NewGuid():N}.db");
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
            SaleService = _scope.ServiceProvider.GetRequiredService<ISaleService>();
        }

        public FinancialManagementDbContext Context { get; }

        public ISaleService SaleService { get; }

        private IPasswordHasher PasswordHasher { get; }

        private IAuthenticationService AuthenticationService { get; }

        public User AddUser(string username, UserRole role)
        {
            var user = new User
            {
                FullName = $"Sale Service Test {username}",
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

        public Client AddClient(string name)
        {
            var client = new Client
            {
                Name = name,
                CreatedAtUtc = WorkflowUtc,
                UpdatedAtUtc = WorkflowUtc
            };
            Context.Clients.Add(client);
            Context.SaveChanges();
            return client;
        }

        public void GrantSalesCreate(User employee)
        {
            Context.UserPermissions.Add(new UserPermission
            {
                UserId = employee.Id,
                Module = PermissionModule.Sales,
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
        public const string SaleReference = "VTE-20260204-ABCDEF123456";
        public const string CustomerPaymentReference = "ENC-20260204-ABCDEF123456";

        public string GenerateSaleReference(DateOnly businessDate) => SaleReference;

        public string GenerateCustomerPaymentReference(DateOnly businessDate) =>
            CustomerPaymentReference;

        public string GeneratePurchaseReference(DateOnly businessDate) =>
            "ACH-20260204-ABCDEF123456";

        public string GenerateSupplierPaymentReference(DateOnly businessDate) =>
            "DEC-20260204-ABCDEF123456";

        public string GenerateInitialCustomerReceivableReference(DateOnly historicalDate) =>
            "CRE-20260101-ABCDEF123456";

        public string GenerateInitialSupplierPayableReference(DateOnly historicalDate) =>
            "DET-20260101-ABCDEF123456";
    }
}
