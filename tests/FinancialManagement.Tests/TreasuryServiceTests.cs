using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Application.CustomerPayments;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class TreasuryServiceTests
{
    private static readonly DateTimeOffset WorkflowUtc =
        new(2026, 6, 30, 23, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly WorkflowLocalDate = new(2026, 7, 1);

    [Fact]
    public async Task Empty_treasury_is_zero_and_uninitialized()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("empty-summary", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var summary = await database.TreasuryService.GetSummaryAsync();

        Assert.True(summary.Success);
        Assert.Equal(TreasurySummaryFailureReason.None, summary.FailureReason);
        Assert.False(summary.IsInitialized);
        Assert.Null(summary.OpeningDate);
        Assert.Equal(0, summary.CashBalanceMinor);
        Assert.Equal(0, summary.BankBalanceMinor);
        Assert.Equal(0, summary.TotalTreasuryMinor);
    }

    [Theory]
    [InlineData(1_000L, 2_000L)]
    [InlineData(-100L, -200L)]
    public async Task Opening_balance_initializes_cash_and_bank_with_signed_amounts(
        long cashAmountMinor,
        long bankAmountMinor)
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser($"opening-{cashAmountMinor}", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.TreasuryService.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(
                WorkflowLocalDate.AddDays(-1),
                cashAmountMinor,
                bankAmountMinor));
        var summary = await database.TreasuryService.GetSummaryAsync();

        Assert.True(result.Success);
        Assert.True(summary.IsInitialized);
        Assert.Equal(WorkflowLocalDate.AddDays(-1), summary.OpeningDate);
        Assert.Equal(cashAmountMinor, summary.CashOpeningMinor);
        Assert.Equal(bankAmountMinor, summary.BankOpeningMinor);
        Assert.Equal(cashAmountMinor, summary.CashBalanceMinor);
        Assert.Equal(bankAmountMinor, summary.BankBalanceMinor);
        Assert.Equal(checked(cashAmountMinor + bankAmountMinor), summary.TotalTreasuryMinor);
        var opening = Assert.Single(database.Context.TreasuryOpeningBalances);
        Assert.Equal(user.Id, opening.CreatedByUserId);
        Assert.Equal(WorkflowUtc, opening.CreatedAtUtc);
    }

    [Fact]
    public async Task Second_opening_balance_is_rejected_without_replacing_first()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("second-opening", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        var first = await database.TreasuryService.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(WorkflowLocalDate, 100, 200));

        var second = await database.TreasuryService.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(WorkflowLocalDate, 300, 400));

        Assert.True(first.Success);
        Assert.False(second.Success);
        Assert.Equal(
            CreateTreasuryOpeningBalanceFailureReason.AlreadyInitialized,
            second.FailureReason);
        var persisted = Assert.Single(database.Context.TreasuryOpeningBalances);
        Assert.Equal(100, persisted.CashAmountMinor);
        Assert.Equal(200, persisted.BankAmountMinor);
    }

    [Fact]
    public async Task Employee_cannot_administer_or_read_treasury_with_all_module_permissions()
    {
        using var database = new TreasuryTestDatabase();
        var employee = database.AddUser("treasury-employee", UserRole.Employe);
        database.GrantAllModulePermissions(employee);
        await database.AuthenticateAsync(employee);

        var opening = await database.TreasuryService.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(WorkflowLocalDate, 100, 200));
        var transfer = await database.TreasuryService.CreateTransferAsync(
            new CreateTreasuryTransferCommand(
                TreasuryAccount.Cash,
                TreasuryAccount.Bank,
                50,
                null));
        var summary = await database.TreasuryService.GetSummaryAsync();

        Assert.Equal(CreateTreasuryOpeningBalanceFailureReason.Forbidden, opening.FailureReason);
        Assert.Equal(CreateTreasuryTransferFailureReason.Forbidden, transfer.FailureReason);
        Assert.Equal(TreasurySummaryFailureReason.Forbidden, summary.FailureReason);
        Assert.Empty(database.Context.TreasuryOpeningBalances);
        Assert.Empty(database.Context.TreasuryTransfers);
    }

    [Fact]
    public async Task Unauthenticated_user_cannot_create_or_read_treasury()
    {
        using var database = new TreasuryTestDatabase();

        var opening = await database.TreasuryService.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(WorkflowLocalDate, 100, 200));
        var transfer = await database.TreasuryService.CreateTransferAsync(
            new CreateTreasuryTransferCommand(
                TreasuryAccount.Cash,
                TreasuryAccount.Bank,
                50,
                null));
        var summary = await database.TreasuryService.GetSummaryAsync();

        Assert.Equal(
            CreateTreasuryOpeningBalanceFailureReason.NotAuthenticated,
            opening.FailureReason);
        Assert.Equal(
            CreateTreasuryTransferFailureReason.NotAuthenticated,
            transfer.FailureReason);
        Assert.Equal(TreasurySummaryFailureReason.NotAuthenticated, summary.FailureReason);
    }

    [Fact]
    public async Task Concurrent_opening_creations_persist_at_most_one_row()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("concurrent-opening", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        using var firstScope = database.CreateIndependentScope();
        using var secondScope = database.CreateIndependentScope();
        var firstContext = firstScope.ServiceProvider
            .GetRequiredService<FinancialManagementDbContext>();
        var secondContext = secondScope.ServiceProvider
            .GetRequiredService<FinancialManagementDbContext>();
        await firstContext.Database.OpenConnectionAsync();
        await secondContext.Database.OpenConnectionAsync();
        Assert.NotSame(
            firstContext.Database.GetDbConnection(),
            secondContext.Database.GetDbConnection());
        var firstService = firstScope.ServiceProvider.GetRequiredService<ITreasuryService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<ITreasuryService>();
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(async () =>
        {
            await gate.Task;
            return await CaptureOpeningAttemptAsync(
                firstService,
                new CreateTreasuryOpeningBalanceCommand(WorkflowLocalDate, 100, 200));
        });
        var secondTask = Task.Run(async () =>
        {
            await gate.Task;
            return await CaptureOpeningAttemptAsync(
                secondService,
                new CreateTreasuryOpeningBalanceCommand(WorkflowLocalDate, 300, 400));
        });

        gate.SetResult();
        var attempts = await Task.WhenAll(firstTask, secondTask);
        firstContext.Database.CloseConnection();
        secondContext.Database.CloseConnection();

        using var verificationScope = database.CreateIndependentScope();
        var context = verificationScope.ServiceProvider
            .GetRequiredService<FinancialManagementDbContext>();
        Assert.Single(attempts, attempt => attempt.Result?.Success == true);
        Assert.Single(await context.TreasuryOpeningBalances.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Mixed_financial_records_produce_expected_cash_bank_and_total()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("mixed-summary", UserRole.Responsable);
        var client = database.AddClient("Mixed Client");
        var supplier = database.AddSupplier("Mixed Supplier");
        await database.AuthenticateAsync(user);
        Assert.True((await database.TreasuryService.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(WorkflowLocalDate, 1_000, 2_000))).Success);
        database.AddCustomerPayment(user, client, 500, PaymentMethod.Cash);
        database.AddCustomerPayment(user, client, 700, PaymentMethod.BankTransfer);
        database.AddSupplierPayment(user, supplier, 200, PaymentMethod.Cash);
        database.AddSupplierPayment(user, supplier, 300, PaymentMethod.BankTransfer);
        database.AddExpense(user, 100, PaymentMethod.Cash);
        database.AddExpense(user, 150, PaymentMethod.BankTransfer);
        Assert.True((await database.TreasuryService.CreateTransferAsync(
            new CreateTreasuryTransferCommand(
                TreasuryAccount.Cash,
                TreasuryAccount.Bank,
                250,
                null))).Success);

        var summary = await database.TreasuryService.GetSummaryAsync();

        Assert.Equal(500, summary.CustomerCashInflowsMinor);
        Assert.Equal(700, summary.CustomerBankInflowsMinor);
        Assert.Equal(200, summary.SupplierCashOutflowsMinor);
        Assert.Equal(300, summary.SupplierBankOutflowsMinor);
        Assert.Equal(100, summary.ExpenseCashOutflowsMinor);
        Assert.Equal(150, summary.ExpenseBankOutflowsMinor);
        Assert.Equal(250, summary.CashToBankTransfersMinor);
        Assert.Equal(0, summary.BankToCashTransfersMinor);
        Assert.Equal(950, summary.CashBalanceMinor);
        Assert.Equal(2_500, summary.BankBalanceMinor);
        Assert.Equal(3_450, summary.TotalTreasuryMinor);
    }

    [Fact]
    public async Task Expenses_are_subtracted_from_their_respective_cash_and_bank_balances()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("isolated-expenses", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        Assert.True((await database.TreasuryService.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(WorkflowLocalDate, 1_000, 2_000))).Success);
        database.AddExpense(user, 100, PaymentMethod.Cash);
        database.AddExpense(user, 150, PaymentMethod.BankTransfer);

        var summary = await database.TreasuryService.GetSummaryAsync();

        Assert.Equal(100, summary.ExpenseCashOutflowsMinor);
        Assert.Equal(150, summary.ExpenseBankOutflowsMinor);
        Assert.Equal(900, summary.CashBalanceMinor);
        Assert.Equal(1_850, summary.BankBalanceMinor);
        Assert.Equal(2_750, summary.TotalTreasuryMinor);
    }

    [Fact]
    public async Task Sales_and_purchases_affect_treasury_only_through_initial_payments()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("initial-payment-summary", UserRole.Responsable);
        var client = database.AddClient("Treasury Sale Client");
        var supplier = database.AddSupplier("Treasury Purchase Supplier");
        await database.AuthenticateAsync(user);

        Assert.True((await database.SaleService.CreateAsync(
            new CreateSaleCommand(client.Id, 1_000, null, null, null))).Success);
        Assert.True((await database.PurchaseService.CreateAsync(
            new CreatePurchaseCommand(supplier.Id, 1_000, null, null, null))).Success);
        var withoutPayments = await database.TreasuryService.GetSummaryAsync();
        Assert.Equal(0, withoutPayments.TotalTreasuryMinor);

        Assert.True((await database.SaleService.CreateAsync(
            new CreateSaleCommand(
                client.Id,
                1_000,
                null,
                null,
                new InitialCustomerPaymentInput(400, PaymentMethod.Cash, null)))).Success);
        Assert.True((await database.PurchaseService.CreateAsync(
            new CreatePurchaseCommand(
                supplier.Id,
                1_000,
                null,
                null,
                new InitialSupplierPaymentInput(300, PaymentMethod.BankTransfer, null)))).Success);

        var summary = await database.TreasuryService.GetSummaryAsync();
        Assert.Equal(400, summary.CashBalanceMinor);
        Assert.Equal(-300, summary.BankBalanceMinor);
        Assert.Equal(100, summary.TotalTreasuryMinor);
    }

    [Fact]
    public async Task Customer_advance_reallocation_does_not_change_treasury()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("customer-advance-summary", UserRole.Responsable);
        var client = database.AddClient("Treasury Advance Client");
        var paidSale = database.AddSale(user, client, 1_000);
        var nextSale = database.AddSale(user, client, 200);
        await database.AuthenticateAsync(user);

        var payment = await database.CustomerPaymentService.CreateAsync(
            new CreateCustomerPaymentCommand(
                client.Id,
                1_200,
                PaymentMethod.Cash,
                null,
                [new CustomerPaymentAllocationInput(paidSale.Id, null, 1_000)]));
        Assert.True(payment.Success);
        var before = await database.TreasuryService.GetSummaryAsync();

        var advance = await database.CustomerAdvanceService.ApplyAsync(
            new ApplyCustomerAdvanceCommand(
                payment.CustomerPaymentId!.Value,
                [new CustomerAdvanceAllocationInput(nextSale.Id, null, 200)]));
        var after = await database.TreasuryService.GetSummaryAsync();

        Assert.True(advance.Success);
        Assert.Equal(1_200, before.CashBalanceMinor);
        Assert.Equal(before.CashBalanceMinor, after.CashBalanceMinor);
        Assert.Equal(before.BankBalanceMinor, after.BankBalanceMinor);
        Assert.Equal(before.TotalTreasuryMinor, after.TotalTreasuryMinor);
    }

    [Fact]
    public async Task Supplier_advance_reallocation_does_not_change_treasury()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("supplier-advance-summary", UserRole.Responsable);
        var supplier = database.AddSupplier("Treasury Advance Supplier");
        var paidPurchase = database.AddPurchase(user, supplier, 1_000);
        var nextPurchase = database.AddPurchase(user, supplier, 200);
        await database.AuthenticateAsync(user);

        var payment = await database.SupplierPaymentService.CreateAsync(
            new CreateSupplierPaymentCommand(
                supplier.Id,
                1_200,
                PaymentMethod.BankTransfer,
                null,
                [new SupplierPaymentAllocationInput(paidPurchase.Id, null, 1_000)]));
        Assert.True(payment.Success);
        var before = await database.TreasuryService.GetSummaryAsync();

        var advance = await database.SupplierAdvanceService.ApplyAsync(
            new ApplySupplierAdvanceCommand(
                payment.SupplierPaymentId!.Value,
                [new SupplierAdvanceAllocationInput(nextPurchase.Id, null, 200)]));
        var after = await database.TreasuryService.GetSummaryAsync();

        Assert.True(advance.Success);
        Assert.Equal(-1_200, before.BankBalanceMinor);
        Assert.Equal(before.CashBalanceMinor, after.CashBalanceMinor);
        Assert.Equal(before.BankBalanceMinor, after.BankBalanceMinor);
        Assert.Equal(before.TotalTreasuryMinor, after.TotalTreasuryMinor);
    }

    [Fact]
    public async Task Initial_obligations_do_not_affect_treasury_until_real_payments()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("initial-obligation-summary", UserRole.Responsable);
        var client = database.AddClient("Initial Treasury Client");
        var supplier = database.AddSupplier("Initial Treasury Supplier");
        await database.AuthenticateAsync(user);

        var receivable = await database.InitialCustomerReceivableService.CreateAsync(
            new CreateInitialCustomerReceivableCommand(
                client.Id,
                500,
                WorkflowLocalDate.AddDays(-30),
                null,
                null));
        var payable = await database.InitialSupplierPayableService.CreateAsync(
            new CreateInitialSupplierPayableCommand(
                supplier.Id,
                700,
                WorkflowLocalDate.AddDays(-30),
                null,
                null));
        Assert.True(receivable.Success);
        Assert.True(payable.Success);
        Assert.Equal(0, (await database.TreasuryService.GetSummaryAsync()).TotalTreasuryMinor);

        Assert.True((await database.CustomerPaymentService.CreateAsync(
            new CreateCustomerPaymentCommand(
                client.Id,
                500,
                PaymentMethod.Cash,
                null,
                [new CustomerPaymentAllocationInput(
                    null,
                    receivable.InitialCustomerReceivableId,
                    500)]))).Success);
        Assert.True((await database.SupplierPaymentService.CreateAsync(
            new CreateSupplierPaymentCommand(
                supplier.Id,
                700,
                PaymentMethod.BankTransfer,
                null,
                [new SupplierPaymentAllocationInput(
                    null,
                    payable.InitialSupplierPayableId,
                    700)]))).Success);

        var summary = await database.TreasuryService.GetSummaryAsync();
        Assert.Equal(500, summary.CashBalanceMinor);
        Assert.Equal(-700, summary.BankBalanceMinor);
        Assert.Equal(-200, summary.TotalTreasuryMinor);
    }

    [Fact]
    public async Task Transfers_preserve_total_and_can_make_source_negative()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("transfer-balance", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        Assert.True((await database.TreasuryService.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(WorkflowLocalDate, 100, 1_000))).Success);
        var before = await database.TreasuryService.GetSummaryAsync();

        var cashToBank = await database.TreasuryService.CreateTransferAsync(
            new CreateTreasuryTransferCommand(
                TreasuryAccount.Cash,
                TreasuryAccount.Bank,
                300,
                "Cash to bank"));
        var middle = await database.TreasuryService.GetSummaryAsync();
        var bankToCash = await database.TreasuryService.CreateTransferAsync(
            new CreateTreasuryTransferCommand(
                TreasuryAccount.Bank,
                TreasuryAccount.Cash,
                300,
                "Bank to cash"));
        var after = await database.TreasuryService.GetSummaryAsync();

        Assert.True(cashToBank.Success);
        Assert.True(bankToCash.Success);
        Assert.Equal(-200, middle.CashBalanceMinor);
        Assert.Equal(1_300, middle.BankBalanceMinor);
        Assert.Equal(before.TotalTreasuryMinor, middle.TotalTreasuryMinor);
        Assert.Equal(100, after.CashBalanceMinor);
        Assert.Equal(1_000, after.BankBalanceMinor);
        Assert.Equal(before.TotalTreasuryMinor, after.TotalTreasuryMinor);
        var firstTransfer = database.Context.TreasuryTransfers
            .Single(item => item.Id == cashToBank.TreasuryTransferId);
        Assert.Equal(user.Id, firstTransfer.CreatedByUserId);
        Assert.Equal(WorkflowUtc, firstTransfer.CreatedAtUtc);
        Assert.Equal(WorkflowLocalDate, firstTransfer.TransferDate);
        Assert.Matches("^TRF-20260701-[0-9A-F]{12}$", firstTransfer.Reference);
    }

    [Theory]
    [MemberData(nameof(InvalidTransfers))]
    public async Task Invalid_transfers_are_rejected(
        CreateTreasuryTransferCommand command,
        CreateTreasuryTransferFailureReason expectedFailure)
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser($"invalid-transfer-{Guid.NewGuid():N}", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.TreasuryService.CreateTransferAsync(command);

        Assert.False(result.Success);
        Assert.Equal(expectedFailure, result.FailureReason);
        Assert.Empty(database.Context.TreasuryTransfers);
    }

    public static TheoryData<
        CreateTreasuryTransferCommand,
        CreateTreasuryTransferFailureReason> InvalidTransfers =>
        new()
        {
            {
                new CreateTreasuryTransferCommand(
                    TreasuryAccount.Cash,
                    TreasuryAccount.Bank,
                    0,
                    null),
                CreateTreasuryTransferFailureReason.InvalidAmount
            },
            {
                new CreateTreasuryTransferCommand(
                    TreasuryAccount.Cash,
                    TreasuryAccount.Bank,
                    -1,
                    null),
                CreateTreasuryTransferFailureReason.InvalidAmount
            },
            {
                new CreateTreasuryTransferCommand(
                    TreasuryAccount.Cash,
                    TreasuryAccount.Cash,
                    100,
                    null),
                CreateTreasuryTransferFailureReason.SameAccount
            },
            {
                new CreateTreasuryTransferCommand(
                    (TreasuryAccount)999,
                    TreasuryAccount.Bank,
                    100,
                    null),
                CreateTreasuryTransferFailureReason.InvalidAccount
            },
            {
                new CreateTreasuryTransferCommand(
                    TreasuryAccount.Cash,
                    (TreasuryAccount)999,
                    100,
                    null),
                CreateTreasuryTransferFailureReason.InvalidAccount
            }
        };

    [Fact]
    public async Task Cancellation_is_propagated_without_treasury_writes()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("treasury-cancel", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.TreasuryService.CreateOpeningBalanceAsync(
                new CreateTreasuryOpeningBalanceCommand(WorkflowLocalDate, 100, 200),
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.TreasuryService.CreateTransferAsync(
                new CreateTreasuryTransferCommand(
                    TreasuryAccount.Cash,
                    TreasuryAccount.Bank,
                    100,
                    null),
                cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.TreasuryService.GetSummaryAsync(cancellation.Token));
        Assert.Empty(database.Context.TreasuryOpeningBalances);
        Assert.Empty(database.Context.TreasuryTransfers);
    }

    [Fact]
    public async Task Summary_combination_overflow_remains_a_technical_error()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("treasury-overflow", UserRole.Responsable);
        var client = database.AddClient("Overflow Client");
        await database.AuthenticateAsync(user);
        Assert.True((await database.TreasuryService.CreateOpeningBalanceAsync(
            new CreateTreasuryOpeningBalanceCommand(
                WorkflowLocalDate,
                long.MaxValue,
                0))).Success);
        database.AddCustomerPayment(user, client, 1, PaymentMethod.Cash);

        await Assert.ThrowsAsync<OverflowException>(() =>
            database.TreasuryService.GetSummaryAsync());
    }

    [Fact]
    public async Task Invalid_persisted_payment_method_is_a_technical_error()
    {
        using var database = new TreasuryTestDatabase();
        var user = database.AddUser("invalid-persisted-method", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        await database.Context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO CustomerPayments
                (Reference, ClientId, PaymentDate, AmountMinor, PaymentMethod,
                 Observation, CreatedByUserId, CreatedAtUtc)
            VALUES
                ({"ENC-INVALID-PERSISTED-METHOD"}, NULL, {WorkflowLocalDate}, {100L},
                 {"Cheque"}, NULL, {user.Id}, {WorkflowUtc})
            """);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.TreasuryService.GetSummaryAsync());
    }

    [Fact]
    public void Default_reference_generator_uses_trf_format()
    {
        using var provider = new ServiceCollection()
            .AddInfrastructure("Data Source=:memory:")
            .BuildServiceProvider();
        var generator = provider.GetRequiredService<IBusinessReferenceGenerator>();

        Assert.Matches(
            "^TRF-20260701-[0-9A-F]{12}$",
            generator.GenerateTreasuryTransferReference(WorkflowLocalDate));
    }

    private static async Task<OpeningAttempt> CaptureOpeningAttemptAsync(
        ITreasuryService service,
        CreateTreasuryOpeningBalanceCommand command)
    {
        try
        {
            return new OpeningAttempt(await service.CreateOpeningBalanceAsync(command), null);
        }
        catch (Exception exception)
        {
            return new OpeningAttempt(null, exception);
        }
    }

    private sealed class TreasuryTestDatabase : IDisposable
    {
        private const string Password = "Treasury test password 42!";

        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _scope;
        private int _referenceSequence;

        public TreasuryTestDatabase()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-Treasury-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(WorkflowUtc));
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
            TreasuryService = _scope.ServiceProvider.GetRequiredService<ITreasuryService>();
            SaleService = _scope.ServiceProvider.GetRequiredService<ISaleService>();
            PurchaseService = _scope.ServiceProvider.GetRequiredService<IPurchaseService>();
            CustomerPaymentService = _scope.ServiceProvider.GetRequiredService<ICustomerPaymentService>();
            SupplierPaymentService = _scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();
            CustomerAdvanceService = _scope.ServiceProvider.GetRequiredService<ICustomerAdvanceService>();
            SupplierAdvanceService = _scope.ServiceProvider.GetRequiredService<ISupplierAdvanceService>();
            InitialCustomerReceivableService = _scope.ServiceProvider.GetRequiredService<IInitialCustomerReceivableService>();
            InitialSupplierPayableService = _scope.ServiceProvider.GetRequiredService<IInitialSupplierPayableService>();
        }

        public FinancialManagementDbContext Context { get; }

        public ITreasuryService TreasuryService { get; }

        public ISaleService SaleService { get; }

        public IPurchaseService PurchaseService { get; }

        public ICustomerPaymentService CustomerPaymentService { get; }

        public ISupplierPaymentService SupplierPaymentService { get; }

        public ICustomerAdvanceService CustomerAdvanceService { get; }

        public ISupplierAdvanceService SupplierAdvanceService { get; }

        public IInitialCustomerReceivableService InitialCustomerReceivableService { get; }

        public IInitialSupplierPayableService InitialSupplierPayableService { get; }

        private IPasswordHasher PasswordHasher { get; }

        private IAuthenticationService AuthenticationService { get; }

        public IServiceScope CreateIndependentScope() => _serviceProvider.CreateScope();

        public User AddUser(string username, UserRole role)
        {
            var user = new User
            {
                FullName = $"Treasury Test {username}",
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

        public Sale AddSale(User user, Client client, long amountMinor)
        {
            var sale = new Sale
            {
                Reference = NextReference("VTE"),
                SaleDate = WorkflowLocalDate,
                TotalAmountMinor = amountMinor,
                ClientId = client.Id,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc,
                UpdatedAtUtc = WorkflowUtc
            };
            Context.Sales.Add(sale);
            Context.SaveChanges();
            return sale;
        }

        public Purchase AddPurchase(User user, Supplier supplier, long amountMinor)
        {
            var purchase = new Purchase
            {
                Reference = NextReference("ACH"),
                PurchaseDate = WorkflowLocalDate,
                TotalAmountMinor = amountMinor,
                SupplierId = supplier.Id,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc,
                UpdatedAtUtc = WorkflowUtc
            };
            Context.Purchases.Add(purchase);
            Context.SaveChanges();
            return purchase;
        }

        public void AddCustomerPayment(
            User user,
            Client client,
            long amountMinor,
            PaymentMethod method)
        {
            Context.CustomerPayments.Add(new CustomerPayment
            {
                Reference = NextReference("ENC"),
                ClientId = client.Id,
                PaymentDate = WorkflowLocalDate,
                AmountMinor = amountMinor,
                PaymentMethod = method,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc
            });
            Context.SaveChanges();
        }

        public void AddSupplierPayment(
            User user,
            Supplier supplier,
            long amountMinor,
            PaymentMethod method)
        {
            Context.SupplierPayments.Add(new SupplierPayment
            {
                Reference = NextReference("DEC"),
                SupplierId = supplier.Id,
                PaymentDate = WorkflowLocalDate,
                AmountMinor = amountMinor,
                PaymentMethod = method,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc
            });
            Context.SaveChanges();
        }

        public void AddExpense(User user, long amountMinor, PaymentMethod method)
        {
            Context.Expenses.Add(new Expense
            {
                Reference = NextReference("DEP"),
                ExpenseDate = WorkflowLocalDate,
                ExpenseCategoryId = 1,
                AmountMinor = amountMinor,
                PaymentMethod = method,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc,
                UpdatedAtUtc = WorkflowUtc
            });
            Context.SaveChanges();
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

        private string NextReference(string prefix) =>
            $"{prefix}-TREASURY-{++_referenceSequence:D4}";

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
            "FinancialManagement-Treasury-Test-UTC-02",
            TimeSpan.FromHours(2),
            "FinancialManagement Treasury Test UTC+02",
            "FinancialManagement Treasury Test UTC+02");

        public override TimeZoneInfo LocalTimeZone => TestTimeZone;

        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record OpeningAttempt(
        CreateTreasuryOpeningBalanceResult? Result,
        Exception? Exception);
}
