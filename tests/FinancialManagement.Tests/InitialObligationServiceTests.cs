using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.InitialCustomerReceivables;
using FinancialManagement.Application.InitialSupplierPayables;
using FinancialManagement.Application.Sales;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class InitialObligationServiceTests
{
    private static readonly DateTimeOffset WorkflowUtc =
        new(2026, 2, 3, 23, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly HistoricalDate = new(2024, 1, 15);

    private static readonly DateOnly HistoricalDueDate = new(2024, 2, 15);

    [Fact]
    public async Task Responsible_creates_customer_receivable_without_financial_side_effects()
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser("receivable-responsable", UserRole.Responsable);
        var client = database.AddClient("Historical Customer");
        await database.AuthenticateAsync(user);

        var result = await database.CustomerReceivableService.CreateAsync(
            new CreateInitialCustomerReceivableCommand(
                client.Id,
                3_000,
                HistoricalDate,
                HistoricalDueDate,
                "Opening customer receivable"));

        Assert.True(result.Success);
        Assert.Equal(CreateInitialCustomerReceivableFailureReason.None, result.FailureReason);
        Assert.Equal(3_000, result.InitialAmountMinor);
        Assert.Equal(
            "CRE-20240115-000000000001",
            result.Reference);
        var receivable = database.Context.InitialCustomerReceivables.Single();
        Assert.Equal(client.Id, receivable.ClientId);
        Assert.Equal(HistoricalDate, receivable.ReceivableDate);
        Assert.Equal(HistoricalDueDate, receivable.DueDate);
        Assert.Equal("Opening customer receivable", receivable.Observation);
        Assert.Equal(user.Id, receivable.CreatedByUserId);
        Assert.Equal(WorkflowUtc, receivable.CreatedAtUtc);
        Assert.Empty(database.Context.Sales);
        Assert.Empty(database.Context.CustomerPayments);
        Assert.Empty(database.Context.CustomerPaymentAllocations);
        Assert.Empty(database.Context.TreasuryOpeningBalances);
        Assert.Empty(database.Context.TreasuryTransfers);
    }

    [Fact]
    public async Task Employee_without_initialization_capability_is_rejected_for_customer_receivable()
    {
        using var database = new InitialObligationTestDatabase();
        var employee = database.AddUser("receivable-employee", UserRole.Employe);
        var client = database.AddClient("Employee Customer");
        await database.AuthenticateAsync(employee);

        var result = await database.CustomerReceivableService.CreateAsync(
            CustomerCommand(client.Id, 1_000));

        Assert.False(result.Success);
        Assert.Equal(CreateInitialCustomerReceivableFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(database.Context.InitialCustomerReceivables);
    }

    [Fact]
    public async Task Employee_with_all_operational_permissions_is_still_rejected_for_customer_receivable()
    {
        using var database = new InitialObligationTestDatabase();
        var employee = database.AddUser("receivable-all-permissions", UserRole.Employe);
        var client = database.AddClient("All Permissions Customer");
        database.GrantAllOperationalPermissions(employee);
        await database.AuthenticateAsync(employee);

        var result = await database.CustomerReceivableService.CreateAsync(
            CustomerCommand(client.Id, 1_000));

        Assert.False(result.Success);
        Assert.Equal(CreateInitialCustomerReceivableFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(database.Context.InitialCustomerReceivables);
    }

    [Fact]
    public async Task Unauthenticated_customer_receivable_creation_is_rejected()
    {
        using var database = new InitialObligationTestDatabase();

        var result = await database.CustomerReceivableService.CreateAsync(
            CustomerCommand(1, 1_000));

        Assert.False(result.Success);
        Assert.Equal(CreateInitialCustomerReceivableFailureReason.NotAuthenticated, result.FailureReason);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(999_999L)]
    public async Task Invalid_or_unknown_client_is_rejected(long clientId)
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser($"receivable-client-{clientId}", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerReceivableService.CreateAsync(
            CustomerCommand(clientId, 1_000));

        Assert.False(result.Success);
        Assert.Equal(CreateInitialCustomerReceivableFailureReason.InvalidClient, result.FailureReason);
        Assert.Empty(database.Context.InitialCustomerReceivables);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Non_positive_customer_receivable_amount_is_rejected(long amountMinor)
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser($"receivable-amount-{amountMinor}", UserRole.Responsable);
        var client = database.AddClient("Invalid Receivable Amount Customer");
        await database.AuthenticateAsync(user);

        var result = await database.CustomerReceivableService.CreateAsync(
            CustomerCommand(client.Id, amountMinor));

        Assert.False(result.Success);
        Assert.Equal(CreateInitialCustomerReceivableFailureReason.InvalidAmount, result.FailureReason);
        Assert.Empty(database.Context.InitialCustomerReceivables);
    }

    [Fact]
    public async Task Multiple_customer_receivables_for_one_client_are_distinct()
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser("receivable-multiple", UserRole.Responsable);
        var client = database.AddClient("Multiple Receivables Customer");
        await database.AuthenticateAsync(user);

        var first = await database.CustomerReceivableService.CreateAsync(
            CustomerCommand(client.Id, 1_000));
        var second = await database.CustomerReceivableService.CreateAsync(
            new CreateInitialCustomerReceivableCommand(
                client.Id,
                500,
                HistoricalDate.AddDays(1),
                null,
                null));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEqual(first.InitialCustomerReceivableId, second.InitialCustomerReceivableId);
        Assert.Equal(2, database.Context.InitialCustomerReceivables.Count());
        Assert.Equal(2, database.Context.InitialCustomerReceivables.Select(item => item.Reference).Distinct().Count());
    }

    [Fact]
    public async Task Customer_receivable_cancellation_is_propagated_without_writes()
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser("receivable-cancelled", UserRole.Responsable);
        var client = database.AddClient("Cancelled Receivable Customer");
        await database.AuthenticateAsync(user);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.CustomerReceivableService.CreateAsync(
                CustomerCommand(client.Id, 1_000),
                cancellation.Token));

        Assert.Empty(database.Context.InitialCustomerReceivables);
    }

    [Fact]
    public async Task Created_customer_receivable_is_consumed_by_customer_payment_service()
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser("receivable-consumption", UserRole.Responsable);
        var client = database.AddClient("Receivable Consumption Customer");
        await database.AuthenticateAsync(user);
        var receivableResult = await database.CustomerReceivableService.CreateAsync(
            CustomerCommand(client.Id, 1_000));

        var firstPayment = await database.CustomerPaymentService.CreateAsync(
            new CreateCustomerPaymentCommand(
                client.Id,
                600,
                PaymentMethod.Cash,
                null,
                [new CustomerPaymentAllocationInput(null, receivableResult.InitialCustomerReceivableId, 600)]));
        var remainingAfterFirst = 1_000 - await database.Context.CustomerPaymentAllocations
            .Where(item => item.InitialCustomerReceivableId == receivableResult.InitialCustomerReceivableId)
            .SumAsync(item => item.AmountMinor);
        var secondPayment = await database.CustomerPaymentService.CreateAsync(
            new CreateCustomerPaymentCommand(
                client.Id,
                400,
                PaymentMethod.Cash,
                null,
                [new CustomerPaymentAllocationInput(null, receivableResult.InitialCustomerReceivableId, 400)]));
        var remainingAfterSecond = 1_000 - await database.Context.CustomerPaymentAllocations
            .Where(item => item.InitialCustomerReceivableId == receivableResult.InitialCustomerReceivableId)
            .SumAsync(item => item.AmountMinor);
        var rejected = await database.CustomerPaymentService.CreateAsync(
            new CreateCustomerPaymentCommand(
                client.Id,
                1,
                PaymentMethod.Cash,
                null,
                [new CustomerPaymentAllocationInput(null, receivableResult.InitialCustomerReceivableId, 1)]));

        Assert.True(firstPayment.Success);
        Assert.Equal(400, remainingAfterFirst);
        Assert.True(secondPayment.Success);
        Assert.Equal(0, remainingAfterSecond);
        Assert.False(rejected.Success);
        Assert.Equal(CreateCustomerPaymentFailureReason.AllocationExceedsOutstanding, rejected.FailureReason);
        Assert.Equal(2, database.Context.CustomerPayments.Count());
        Assert.Equal(2, database.Context.CustomerPaymentAllocations.Count());
    }

    [Fact]
    public async Task Responsible_creates_supplier_payable_without_financial_side_effects()
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser("payable-responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Historical Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPayableService.CreateAsync(
            new CreateInitialSupplierPayableCommand(
                supplier.Id,
                5_000,
                HistoricalDate,
                HistoricalDueDate,
                "Opening supplier payable"));

        Assert.True(result.Success);
        Assert.Equal(CreateInitialSupplierPayableFailureReason.None, result.FailureReason);
        Assert.Equal(5_000, result.InitialAmountMinor);
        Assert.Equal("DET-20240115-000000000001", result.Reference);
        var payable = database.Context.InitialSupplierPayables.Single();
        Assert.Equal(supplier.Id, payable.SupplierId);
        Assert.Equal(HistoricalDate, payable.PayableDate);
        Assert.Equal(HistoricalDueDate, payable.DueDate);
        Assert.Equal("Opening supplier payable", payable.Observation);
        Assert.Equal(user.Id, payable.CreatedByUserId);
        Assert.Equal(WorkflowUtc, payable.CreatedAtUtc);
        Assert.Empty(database.Context.Purchases);
        Assert.Empty(database.Context.SupplierPayments);
        Assert.Empty(database.Context.SupplierPaymentAllocations);
        Assert.Empty(database.Context.TreasuryOpeningBalances);
        Assert.Empty(database.Context.TreasuryTransfers);
    }

    [Fact]
    public async Task Initial_obligation_creation_does_not_consume_existing_advances()
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser("initial-no-advance-use", UserRole.Responsable);
        var client = database.AddClient("Advance Customer");
        var supplier = database.AddSupplier("Advance Supplier");
        database.Context.CustomerPayments.Add(new CustomerPayment
        {
            Reference = "ENC-HISTORICAL-ADVANCE",
            ClientId = client.Id,
            PaymentDate = HistoricalDate,
            AmountMinor = 900,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = user.Id,
            CreatedAtUtc = WorkflowUtc
        });
        database.Context.SupplierPayments.Add(new SupplierPayment
        {
            Reference = "DEC-HISTORICAL-ADVANCE",
            SupplierId = supplier.Id,
            PaymentDate = HistoricalDate,
            AmountMinor = 800,
            PaymentMethod = PaymentMethod.BankTransfer,
            CreatedByUserId = user.Id,
            CreatedAtUtc = WorkflowUtc
        });
        await database.Context.SaveChangesAsync();
        await database.AuthenticateAsync(user);

        var receivable = await database.CustomerReceivableService.CreateAsync(
            CustomerCommand(client.Id, 1_000));
        var payable = await database.SupplierPayableService.CreateAsync(
            SupplierCommand(supplier.Id, 1_000));

        Assert.True(receivable.Success);
        Assert.True(payable.Success);
        Assert.Equal(900, database.Context.CustomerPayments.Single().AmountMinor);
        Assert.Equal(800, database.Context.SupplierPayments.Single().AmountMinor);
        Assert.Empty(database.Context.CustomerPaymentAllocations);
        Assert.Empty(database.Context.SupplierPaymentAllocations);
    }

    [Fact]
    public async Task Employee_without_initialization_capability_is_rejected_for_supplier_payable()
    {
        using var database = new InitialObligationTestDatabase();
        var employee = database.AddUser("payable-employee", UserRole.Employe);
        var supplier = database.AddSupplier("Employee Supplier");
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierPayableService.CreateAsync(
            SupplierCommand(supplier.Id, 1_000));

        Assert.False(result.Success);
        Assert.Equal(CreateInitialSupplierPayableFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(database.Context.InitialSupplierPayables);
    }

    [Fact]
    public async Task Employee_with_all_operational_permissions_is_still_rejected_for_supplier_payable()
    {
        using var database = new InitialObligationTestDatabase();
        var employee = database.AddUser("payable-all-permissions", UserRole.Employe);
        var supplier = database.AddSupplier("All Permissions Supplier");
        database.GrantAllOperationalPermissions(employee);
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierPayableService.CreateAsync(
            SupplierCommand(supplier.Id, 1_000));

        Assert.False(result.Success);
        Assert.Equal(CreateInitialSupplierPayableFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(database.Context.InitialSupplierPayables);
    }

    [Fact]
    public async Task Unauthenticated_supplier_payable_creation_is_rejected()
    {
        using var database = new InitialObligationTestDatabase();

        var result = await database.SupplierPayableService.CreateAsync(
            SupplierCommand(1, 1_000));

        Assert.False(result.Success);
        Assert.Equal(CreateInitialSupplierPayableFailureReason.NotAuthenticated, result.FailureReason);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(999_999L)]
    public async Task Invalid_or_unknown_supplier_is_rejected(long supplierId)
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser($"payable-supplier-{supplierId}", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPayableService.CreateAsync(
            SupplierCommand(supplierId, 1_000));

        Assert.False(result.Success);
        Assert.Equal(CreateInitialSupplierPayableFailureReason.InvalidSupplier, result.FailureReason);
        Assert.Empty(database.Context.InitialSupplierPayables);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Non_positive_supplier_payable_amount_is_rejected(long amountMinor)
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser($"payable-amount-{amountMinor}", UserRole.Responsable);
        var supplier = database.AddSupplier("Invalid Payable Amount Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPayableService.CreateAsync(
            SupplierCommand(supplier.Id, amountMinor));

        Assert.False(result.Success);
        Assert.Equal(CreateInitialSupplierPayableFailureReason.InvalidAmount, result.FailureReason);
        Assert.Empty(database.Context.InitialSupplierPayables);
    }

    [Fact]
    public async Task Multiple_supplier_payables_for_one_supplier_are_distinct()
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser("payable-multiple", UserRole.Responsable);
        var supplier = database.AddSupplier("Multiple Payables Supplier");
        await database.AuthenticateAsync(user);

        var first = await database.SupplierPayableService.CreateAsync(
            SupplierCommand(supplier.Id, 300));
        var second = await database.SupplierPayableService.CreateAsync(
            new CreateInitialSupplierPayableCommand(
                supplier.Id,
                700,
                HistoricalDate.AddDays(1),
                null,
                null));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEqual(first.InitialSupplierPayableId, second.InitialSupplierPayableId);
        Assert.Equal(2, database.Context.InitialSupplierPayables.Count());
        Assert.Equal(2, database.Context.InitialSupplierPayables.Select(item => item.Reference).Distinct().Count());
    }

    [Fact]
    public async Task Supplier_payable_cancellation_is_propagated_without_writes()
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser("payable-cancelled", UserRole.Responsable);
        var supplier = database.AddSupplier("Cancelled Payable Supplier");
        await database.AuthenticateAsync(user);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SupplierPayableService.CreateAsync(
                SupplierCommand(supplier.Id, 1_000),
                cancellation.Token));

        Assert.Empty(database.Context.InitialSupplierPayables);
    }

    [Fact]
    public async Task Created_supplier_payable_is_consumed_by_supplier_payment_service()
    {
        using var database = new InitialObligationTestDatabase();
        var user = database.AddUser("payable-consumption", UserRole.Responsable);
        var supplier = database.AddSupplier("Payable Consumption Supplier");
        await database.AuthenticateAsync(user);
        var payableResult = await database.SupplierPayableService.CreateAsync(
            SupplierCommand(supplier.Id, 1_000));

        var firstPayment = await database.SupplierPaymentService.CreateAsync(
            new CreateSupplierPaymentCommand(
                supplier.Id,
                600,
                PaymentMethod.Cash,
                null,
                [new SupplierPaymentAllocationInput(null, payableResult.InitialSupplierPayableId, 600)]));
        var remainingAfterFirst = 1_000 - await database.Context.SupplierPaymentAllocations
            .Where(item => item.InitialSupplierPayableId == payableResult.InitialSupplierPayableId)
            .SumAsync(item => item.AmountMinor);
        var secondPayment = await database.SupplierPaymentService.CreateAsync(
            new CreateSupplierPaymentCommand(
                supplier.Id,
                400,
                PaymentMethod.Cash,
                null,
                [new SupplierPaymentAllocationInput(null, payableResult.InitialSupplierPayableId, 400)]));
        var remainingAfterSecond = 1_000 - await database.Context.SupplierPaymentAllocations
            .Where(item => item.InitialSupplierPayableId == payableResult.InitialSupplierPayableId)
            .SumAsync(item => item.AmountMinor);
        var rejected = await database.SupplierPaymentService.CreateAsync(
            new CreateSupplierPaymentCommand(
                supplier.Id,
                1,
                PaymentMethod.Cash,
                null,
                [new SupplierPaymentAllocationInput(null, payableResult.InitialSupplierPayableId, 1)]));

        Assert.True(firstPayment.Success);
        Assert.Equal(400, remainingAfterFirst);
        Assert.True(secondPayment.Success);
        Assert.Equal(0, remainingAfterSecond);
        Assert.False(rejected.Success);
        Assert.Equal(CreateSupplierPaymentFailureReason.AllocationExceedsOutstanding, rejected.FailureReason);
        Assert.Equal(2, database.Context.SupplierPayments.Count());
        Assert.Equal(2, database.Context.SupplierPaymentAllocations.Count());
    }

    [Fact]
    public void Default_reference_generator_uses_historical_cre_and_det_prefixes()
    {
        using var provider = new ServiceCollection()
            .AddInfrastructure("Data Source=:memory:")
            .BuildServiceProvider();
        var generator = provider.GetRequiredService<IBusinessReferenceGenerator>();

        Assert.Matches("^CRE-20240115-[0-9A-F]{12}$", generator.GenerateInitialCustomerReceivableReference(HistoricalDate));
        Assert.Matches("^DET-20240115-[0-9A-F]{12}$", generator.GenerateInitialSupplierPayableReference(HistoricalDate));
    }

    private static CreateInitialCustomerReceivableCommand CustomerCommand(
        long clientId,
        long amountMinor) =>
        new(clientId, amountMinor, HistoricalDate, null, null);

    private static CreateInitialSupplierPayableCommand SupplierCommand(
        long supplierId,
        long amountMinor) =>
        new(supplierId, amountMinor, HistoricalDate, null, null);

    private sealed class InitialObligationTestDatabase : IDisposable
    {
        private const string Password = "Initial obligation test password 42!";

        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _scope;

        public InitialObligationTestDatabase()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-InitialObligations-{Guid.NewGuid():N}.db");
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
            AuthenticationService = _scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            CustomerReceivableService = _scope.ServiceProvider.GetRequiredService<IInitialCustomerReceivableService>();
            SupplierPayableService = _scope.ServiceProvider.GetRequiredService<IInitialSupplierPayableService>();
            CustomerPaymentService = _scope.ServiceProvider.GetRequiredService<ICustomerPaymentService>();
            SupplierPaymentService = _scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();
        }

        public FinancialManagementDbContext Context { get; }

        public IInitialCustomerReceivableService CustomerReceivableService { get; }

        public IInitialSupplierPayableService SupplierPayableService { get; }

        public ICustomerPaymentService CustomerPaymentService { get; }

        public ISupplierPaymentService SupplierPaymentService { get; }

        private IPasswordHasher PasswordHasher { get; }

        private IAuthenticationService AuthenticationService { get; }

        public User AddUser(string username, UserRole role)
        {
            var user = new User
            {
                FullName = $"Initial Obligation Test {username}",
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

        public void GrantAllOperationalPermissions(User employee)
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
            "FinancialManagement-Initial-Test-UTC-02",
            TimeSpan.FromHours(2),
            "FinancialManagement Initial Test UTC+02",
            "FinancialManagement Initial Test UTC+02");

        public override TimeZoneInfo LocalTimeZone => TestTimeZone;

        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class DeterministicReferenceGenerator : IBusinessReferenceGenerator
    {
        private int _saleSequence;
        private int _customerPaymentSequence;
        private int _purchaseSequence;
        private int _supplierPaymentSequence;
        private int _customerReceivableSequence;
        private int _supplierPayableSequence;

        public string GenerateSaleReference(DateOnly businessDate) =>
            $"VTE-{businessDate:yyyyMMdd}-{Interlocked.Increment(ref _saleSequence):X12}";

        public string GenerateCustomerPaymentReference(DateOnly businessDate) =>
            $"ENC-{businessDate:yyyyMMdd}-{Interlocked.Increment(ref _customerPaymentSequence):X12}";

        public string GeneratePurchaseReference(DateOnly businessDate) =>
            $"ACH-{businessDate:yyyyMMdd}-{Interlocked.Increment(ref _purchaseSequence):X12}";

        public string GenerateSupplierPaymentReference(DateOnly businessDate) =>
            $"DEC-{businessDate:yyyyMMdd}-{Interlocked.Increment(ref _supplierPaymentSequence):X12}";

        public string GenerateInitialCustomerReceivableReference(DateOnly historicalDate) =>
            $"CRE-{historicalDate:yyyyMMdd}-{Interlocked.Increment(ref _customerReceivableSequence):X12}";

        public string GenerateInitialSupplierPayableReference(DateOnly historicalDate) =>
            $"DET-{historicalDate:yyyyMMdd}-{Interlocked.Increment(ref _supplierPayableSequence):X12}";

        public string GenerateTreasuryTransferReference(DateOnly businessDate) =>
            $"TRF-{businessDate:yyyyMMdd}-000000000001";
    }
}
