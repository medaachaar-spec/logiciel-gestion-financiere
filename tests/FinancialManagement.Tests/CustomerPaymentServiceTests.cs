using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Application.InitialCustomerReceivables;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class CustomerPaymentServiceTests
{
    private static readonly DateTimeOffset WorkflowUtc =
        new(2026, 2, 3, 23, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly WorkflowLocalDate = new(2026, 2, 4);

    [Fact]
    public async Task Existing_sale_allocations_are_included_in_outstanding()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("responsable-sale-outstanding", UserRole.Responsable);
        var client = database.AddClient("Sale Outstanding Client");
        var sale = database.AddSale(user, client, 1_000);
        database.AddExistingPayment(
            user,
            client,
            500,
            SaleAllocation(sale.Id, 200),
            SaleAllocation(sale.Id, 300));
        await database.AuthenticateAsync(user);
        var paymentsBefore = database.Context.CustomerPayments.Count();
        var allocationsBefore = database.Context.CustomerPaymentAllocations.Count();

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 500, SaleAllocation(sale.Id, 500)));

        AssertSuccessfulPayment(database, result, client.Id, 500, 500, 0, 1);
        Assert.Equal(paymentsBefore + 1, database.Context.CustomerPayments.Count());
        Assert.Equal(allocationsBefore + 1, database.Context.CustomerPaymentAllocations.Count());
        Assert.Equal(
            1_000,
            database.Context.CustomerPaymentAllocations
                .Where(allocation => allocation.SaleId == sale.Id)
                .Sum(allocation => allocation.AmountMinor));
        Assert.Empty(database.Context.UserPermissions);
    }

    [Fact]
    public async Task Payment_can_be_explicitly_split_across_two_sales()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("responsable-two-sales", UserRole.Responsable);
        var client = database.AddClient("Two Sales Client");
        var firstSale = database.AddSale(user, client, 600);
        var secondSale = database.AddSale(user, client, 500);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(
                client.Id,
                750,
                SaleAllocation(firstSale.Id, 600),
                SaleAllocation(secondSale.Id, 150)));

        AssertSuccessfulPayment(database, result, client.Id, 750, 750, 0, 2);
        var allocations = database.Context.CustomerPaymentAllocations
            .OrderBy(allocation => allocation.SaleId)
            .ToArray();
        Assert.Equal(600, allocations.Single(item => item.SaleId == firstSale.Id).AmountMinor);
        Assert.Equal(150, allocations.Single(item => item.SaleId == secondSale.Id).AmountMinor);
    }

    [Fact]
    public async Task Payment_can_target_sale_and_initial_receivable()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("responsable-mixed-targets", UserRole.Responsable);
        var client = database.AddClient("Mixed Targets Client");
        var sale = database.AddSale(user, client, 600);
        var receivable = database.AddReceivable(user, client, 500);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(
                client.Id,
                800,
                SaleAllocation(sale.Id, 600),
                ReceivableAllocation(receivable.Id, 200)));

        AssertSuccessfulPayment(database, result, client.Id, 800, 800, 0, 2);
        Assert.Contains(
            database.Context.CustomerPaymentAllocations,
            allocation => allocation.SaleId == sale.Id
                && allocation.InitialCustomerReceivableId is null);
        Assert.Contains(
            database.Context.CustomerPaymentAllocations,
            allocation => allocation.SaleId is null
                && allocation.InitialCustomerReceivableId == receivable.Id);
    }

    [Fact]
    public async Task Payment_surplus_remains_unallocated_advance()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("responsable-advance", UserRole.Responsable);
        var client = database.AddClient("Advance Client");
        var sale = database.AddSale(user, client, 850);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 1_000, SaleAllocation(sale.Id, 850)));

        AssertSuccessfulPayment(database, result, client.Id, 1_000, 850, 150, 1);
    }

    [Fact]
    public async Task Allocations_exceeding_payment_are_rejected_without_writes()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("allocations-exceed-payment", UserRole.Responsable);
        var client = database.AddClient("Allocation Sum Client");
        var firstSale = database.AddSale(user, client, 700);
        var secondSale = database.AddSale(user, client, 500);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(
                client.Id,
                1_000,
                SaleAllocation(firstSale.Id, 700),
                SaleAllocation(secondSale.Id, 400)));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.AllocationsExceedPayment);
    }

    [Theory]
    [InlineData(500L, true)]
    [InlineData(501L, false)]
    public async Task Sale_outstanding_boundary_is_enforced(
        long requestedAmountMinor,
        bool shouldSucceed)
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser($"sale-boundary-{requestedAmountMinor}", UserRole.Responsable);
        var client = database.AddClient("Sale Boundary Client");
        var sale = database.AddSale(user, client, 1_000);
        database.AddExistingPayment(
            user,
            client,
            500,
            SaleAllocation(sale.Id, 200),
            SaleAllocation(sale.Id, 300));
        await database.AuthenticateAsync(user);
        var paymentsBefore = database.Context.CustomerPayments.Count();
        var allocationsBefore = database.Context.CustomerPaymentAllocations.Count();

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(
                client.Id,
                requestedAmountMinor,
                SaleAllocation(sale.Id, requestedAmountMinor)));

        if (shouldSucceed)
        {
            Assert.True(result.Success);
            Assert.Equal(500, result.AllocatedAmountMinor);
            return;
        }

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.AllocationExceedsOutstanding,
            paymentsBefore,
            allocationsBefore);
    }

    [Theory]
    [InlineData(500L, true)]
    [InlineData(501L, false)]
    public async Task Initial_receivable_outstanding_boundary_is_enforced(
        long requestedAmountMinor,
        bool shouldSucceed)
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser(
            $"receivable-boundary-{requestedAmountMinor}",
            UserRole.Responsable);
        var client = database.AddClient("Receivable Boundary Client");
        var receivable = database.AddReceivable(user, client, 1_000);
        database.AddExistingPayment(
            user,
            client,
            500,
            ReceivableAllocation(receivable.Id, 200),
            ReceivableAllocation(receivable.Id, 300));
        await database.AuthenticateAsync(user);
        var paymentsBefore = database.Context.CustomerPayments.Count();
        var allocationsBefore = database.Context.CustomerPaymentAllocations.Count();

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(
                client.Id,
                requestedAmountMinor,
                ReceivableAllocation(receivable.Id, requestedAmountMinor)));

        if (shouldSucceed)
        {
            Assert.True(result.Success);
            Assert.Equal(500, result.AllocatedAmountMinor);
            return;
        }

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.AllocationExceedsOutstanding,
            paymentsBefore,
            allocationsBefore);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fully_allocated_obligation_rejects_another_allocation(bool targetSale)
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser($"fully-allocated-{targetSale}", UserRole.Responsable);
        var client = database.AddClient("Fully Allocated Client");
        var target = targetSale
            ? SaleAllocation(database.AddSale(user, client, 500).Id, 500)
            : ReceivableAllocation(database.AddReceivable(user, client, 500).Id, 500);
        database.AddExistingPayment(user, client, 500, target);
        await database.AuthenticateAsync(user);
        var paymentsBefore = database.Context.CustomerPayments.Count();
        var allocationsBefore = database.Context.CustomerPaymentAllocations.Count();
        var requested = targetSale
            ? SaleAllocation(target.SaleId!.Value, 1)
            : ReceivableAllocation(target.InitialCustomerReceivableId!.Value, 1);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 1, requested));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.AllocationExceedsOutstanding,
            paymentsBefore,
            allocationsBefore);
    }

    [Fact]
    public async Task Outstanding_includes_initial_payment_created_by_sales_core()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("sales-core-payment", UserRole.Responsable);
        var client = database.AddClient("Sales Core Payment Client");
        await database.AuthenticateAsync(user);
        var saleResult = await database.SaleService.CreateAsync(new CreateSaleCommand(
            client.Id,
            1_000,
            null,
            null,
            new InitialCustomerPaymentInput(400, PaymentMethod.Cash, null)));

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 600, SaleAllocation(saleResult.SaleId!.Value, 600)));

        Assert.True(result.Success);
        Assert.Equal(2, database.Context.CustomerPayments.Count());
        Assert.Equal(
            1_000,
            database.Context.CustomerPaymentAllocations
                .Where(allocation => allocation.SaleId == saleResult.SaleId)
                .Sum(allocation => allocation.AmountMinor));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Obligation_from_another_client_is_rejected(bool targetSale)
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser($"different-client-{targetSale}", UserRole.Responsable);
        var payingClient = database.AddClient("Paying Client");
        var otherClient = database.AddClient("Other Client");
        var input = targetSale
            ? SaleAllocation(database.AddSale(user, otherClient, 500).Id, 100)
            : ReceivableAllocation(database.AddReceivable(user, otherClient, 500).Id, 100);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(payingClient.Id, 100, input));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.ObligationBelongsToDifferentClient);
    }

    [Fact]
    public async Task Anonymous_sale_cannot_receive_standalone_payment()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("anonymous-sale-target", UserRole.Responsable);
        var client = database.AddClient("Known Payment Client");
        var anonymousSale = database.AddSale(user, client: null, 500);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 100, SaleAllocation(anonymousSale.Id, 100)));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.ObligationBelongsToDifferentClient);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(999_999L)]
    public async Task Invalid_or_unknown_client_is_rejected(long clientId)
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser($"invalid-client-{clientId}", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(clientId, 100, SaleAllocation(1, 100)));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.InvalidClient);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Non_positive_payment_is_rejected(long amountMinor)
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser($"invalid-payment-{amountMinor}", UserRole.Responsable);
        var client = database.AddClient("Invalid Payment Client");
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, amountMinor, SaleAllocation(1, 1)));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.InvalidAmount);
    }

    [Fact]
    public async Task Undefined_payment_method_is_rejected()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("invalid-method", UserRole.Responsable);
        var client = database.AddClient("Invalid Method Client");
        await database.AuthenticateAsync(user);
        var command = new CreateCustomerPaymentCommand(
            client.Id,
            100,
            (PaymentMethod)999,
            null,
            [SaleAllocation(1, 100)]);

        var result = await database.CustomerPaymentService.CreateAsync(command);

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.InvalidPaymentMethod);
    }

    [Theory]
    [MemberData(nameof(MissingAllocationCollections))]
    public async Task Missing_allocations_are_rejected(
        IReadOnlyCollection<CustomerPaymentAllocationInput>? allocations)
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("missing-allocations", UserRole.Responsable);
        var client = database.AddClient("Missing Allocations Client");
        await database.AuthenticateAsync(user);
        var command = new CreateCustomerPaymentCommand(
            client.Id,
            100,
            PaymentMethod.Cash,
            null,
            allocations);

        var result = await database.CustomerPaymentService.CreateAsync(command);

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.MissingAllocations);
    }

    public static TheoryData<IReadOnlyCollection<CustomerPaymentAllocationInput>?>
        MissingAllocationCollections =>
        new()
        {
            null,
            Array.Empty<CustomerPaymentAllocationInput>()
        };

    [Theory]
    [MemberData(nameof(InvalidAllocations))]
    public async Task Invalid_allocation_structure_is_rejected(
        CustomerPaymentAllocationInput allocation)
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser(
            $"invalid-allocation-{Guid.NewGuid():N}",
            UserRole.Responsable);
        var client = database.AddClient("Invalid Allocation Client");
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 100, allocation));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.InvalidAllocation);
    }

    public static TheoryData<CustomerPaymentAllocationInput> InvalidAllocations =>
        new()
        {
            new CustomerPaymentAllocationInput(null, null, 100),
            new CustomerPaymentAllocationInput(1, 1, 100),
            new CustomerPaymentAllocationInput(1, null, 0),
            new CustomerPaymentAllocationInput(1, null, -1),
            new CustomerPaymentAllocationInput(0, null, 100),
            new CustomerPaymentAllocationInput(null, -1, 100)
        };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Duplicate_allocation_target_is_rejected(bool targetSale)
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser($"duplicate-target-{targetSale}", UserRole.Responsable);
        var client = database.AddClient("Duplicate Target Client");
        var first = targetSale ? SaleAllocation(1, 50) : ReceivableAllocation(1, 50);
        var second = targetSale ? SaleAllocation(1, 50) : ReceivableAllocation(1, 50);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 100, first, second));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.DuplicateAllocationTarget);
    }

    [Fact]
    public async Task Missing_obligation_is_rejected()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("missing-obligation", UserRole.Responsable);
        var client = database.AddClient("Missing Obligation Client");
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 100, SaleAllocation(999_999, 100)));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.ObligationNotFound);
    }

    [Fact]
    public async Task Unauthenticated_user_is_rejected()
    {
        using var database = new CustomerPaymentServiceTestDatabase();

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(1, 100, SaleAllocation(1, 100)));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.NotAuthenticated);
    }

    [Fact]
    public async Task Employee_without_customer_payment_create_is_rejected()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var employee = database.AddUser("employee-denied", UserRole.Employe);
        var client = database.AddClient("Employee Denied Client");
        var sale = database.AddSale(employee, client, 100);
        await database.AuthenticateAsync(employee);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 100, SaleAllocation(sale.Id, 100)));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.Forbidden);
    }

    [Fact]
    public async Task Employee_with_customer_payment_create_needs_no_sales_permission()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var employee = database.AddUser("employee-payment-only", UserRole.Employe);
        var client = database.AddClient("Employee Payment Client");
        var sale = database.AddSale(employee, client, 100);
        database.GrantCustomerPaymentsCreate(employee);
        await database.AuthenticateAsync(employee);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 100, SaleAllocation(sale.Id, 100)));

        Assert.True(result.Success);
        Assert.DoesNotContain(
            database.Context.UserPermissions,
            permission => permission.Module == PermissionModule.Sales);
    }

    [Fact]
    public async Task Current_user_time_observation_client_and_reference_are_persisted()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("metadata-user", UserRole.Responsable);
        var client = database.AddClient("Metadata Client");
        var sale = database.AddSale(user, client, 500);
        await database.AuthenticateAsync(user);
        var command = new CreateCustomerPaymentCommand(
            client.Id,
            500,
            PaymentMethod.BankTransfer,
            "Standalone payment observation",
            [SaleAllocation(sale.Id, 500)]);

        var result = await database.CustomerPaymentService.CreateAsync(command);

        Assert.True(result.Success);
        Assert.Equal(DeterministicReferenceGenerator.FirstCustomerPaymentReference, result.CustomerPaymentReference);
        var payment = database.Context.CustomerPayments.AsNoTracking().Single();
        Assert.Equal(client.Id, payment.ClientId);
        Assert.Equal(user.Id, payment.CreatedByUserId);
        Assert.Equal(WorkflowLocalDate, payment.PaymentDate);
        Assert.Equal(WorkflowUtc, payment.CreatedAtUtc);
        Assert.Equal(PaymentMethod.BankTransfer, payment.PaymentMethod);
        Assert.Equal("Standalone payment observation", payment.Observation);
        Assert.Equal(result.CustomerPaymentReference, payment.Reference);
    }

    [Fact]
    public async Task Reference_collision_rolls_back_payment_and_allocations()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("rollback-user", UserRole.Responsable);
        var client = database.AddClient("Rollback Client");
        var sale = database.AddSale(user, client, 500);
        database.Context.CustomerPayments.Add(new CustomerPayment
        {
            Reference = DeterministicReferenceGenerator.FirstCustomerPaymentReference,
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
            database.CustomerPaymentService.CreateAsync(
                CreateCommand(client.Id, 500, SaleAllocation(sale.Id, 500))));

        database.Context.ChangeTracker.Clear();
        Assert.Single(database.Context.CustomerPayments);
        Assert.Empty(database.Context.CustomerPaymentAllocations);
    }

    [Fact]
    public async Task Existing_advance_and_untargeted_obligation_remain_unchanged()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("existing-advance", UserRole.Responsable);
        var client = database.AddClient("Existing Advance Client");
        var previouslyTargetedSale = database.AddSale(user, client, 200);
        var requestedSale = database.AddSale(user, client, 300);
        var untouchedSale = database.AddSale(user, client, 400);
        var existingPayment = database.AddExistingPayment(
            user,
            client,
            1_000,
            SaleAllocation(previouslyTargetedSale.Id, 200));
        var existingAllocationId = database.Context.CustomerPaymentAllocations.Single().Id;
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 300, SaleAllocation(requestedSale.Id, 300)));

        Assert.True(result.Success);
        var existingPaymentAfter = database.Context.CustomerPayments
            .AsNoTracking()
            .Single(payment => payment.Id == existingPayment.Id);
        var existingAllocationAfter = database.Context.CustomerPaymentAllocations
            .AsNoTracking()
            .Single(allocation => allocation.Id == existingAllocationId);
        Assert.Equal(1_000, existingPaymentAfter.AmountMinor);
        Assert.Equal(200, existingAllocationAfter.AmountMinor);
        Assert.DoesNotContain(
            database.Context.CustomerPaymentAllocations,
            allocation => allocation.SaleId == untouchedSale.Id);
        Assert.Single(
            database.Context.CustomerPaymentAllocations,
            allocation => allocation.CustomerPaymentId == result.CustomerPaymentId);
    }

    [Fact]
    public async Task Corrupted_existing_overallocation_returns_integrity_failure()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("corrupted-overallocation", UserRole.Responsable);
        var client = database.AddClient("Corrupted Client");
        var sale = database.AddSale(user, client, 1_000);
        database.AddExistingPayment(
            user,
            client,
            1_200,
            SaleAllocation(sale.Id, 1_200));
        await database.AuthenticateAsync(user);
        var paymentsBefore = database.Context.CustomerPayments.Count();
        var allocationsBefore = database.Context.CustomerPaymentAllocations.Count();

        var result = await database.CustomerPaymentService.CreateAsync(
                CreateCommand(client.Id, 100, SaleAllocation(sale.Id, 100)));
        Assert.False(result.Success);
        Assert.Equal(CreateCustomerPaymentFailureReason.DataIntegrityViolation, result.FailureReason);

        Assert.Equal(paymentsBefore, database.Context.CustomerPayments.Count());
        Assert.Equal(allocationsBefore, database.Context.CustomerPaymentAllocations.Count());
    }

    [Fact]
    public async Task Allocation_sum_validation_does_not_overflow_long()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("long-overflow", UserRole.Responsable);
        var client = database.AddClient("Long Overflow Client");
        await database.AuthenticateAsync(user);

        var result = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(
                client.Id,
                long.MaxValue,
                SaleAllocation(1, long.MaxValue - 1),
                SaleAllocation(2, 2)));

        AssertFunctionalFailure(
            database,
            result,
            CreateCustomerPaymentFailureReason.AllocationsExceedPayment);
    }

    [Fact]
    public async Task Concurrent_payments_cannot_silently_overallocate_same_sale()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("concurrent-payments", UserRole.Responsable);
        var client = database.AddClient("Concurrent Payment Client");
        var sale = database.AddSale(user, client, 1_000);
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
        var firstService = firstScope.ServiceProvider
            .GetRequiredService<ICustomerPaymentService>();
        var secondService = secondScope.ServiceProvider
            .GetRequiredService<ICustomerPaymentService>();
        var command = CreateCommand(client.Id, 1_000, SaleAllocation(sale.Id, 1_000));
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstAttemptTask = Task.Run(async () =>
        {
            await startGate.Task;
            return await CaptureAttemptAsync(firstService, command);
        });
        var secondAttemptTask = Task.Run(async () =>
        {
            await startGate.Task;
            return await CaptureAttemptAsync(secondService, command);
        });

        startGate.SetResult();
        var attempts = await Task.WhenAll(firstAttemptTask, secondAttemptTask);

        Assert.Single(attempts, attempt => attempt.Result?.Success == true);
        Assert.Single(attempts, attempt => attempt.Result?.Success != true);
        Console.WriteLine($"Workflow A: {DescribeAttempt(attempts[0])}");
        Console.WriteLine($"Workflow B: {DescribeAttempt(attempts[1])}");

        firstContext.Database.CloseConnection();
        secondContext.Database.CloseConnection();
        using var verificationScope = database.CreateIndependentScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<FinancialManagementDbContext>();
        var persistedSale = await verificationContext.Sales
            .AsNoTracking()
            .SingleAsync(item => item.Id == sale.Id);
        var finalAllocatedMinor = await verificationContext.CustomerPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.SaleId == sale.Id)
            .SumAsync(allocation => allocation.AmountMinor);
        var persistedPayments = await verificationContext.CustomerPayments
            .AsNoTracking()
            .Where(payment => payment.ClientId == client.Id)
            .CountAsync();
        var persistedAllocations = await verificationContext.CustomerPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.SaleId == sale.Id)
            .CountAsync();

        Assert.True(finalAllocatedMinor <= persistedSale.TotalAmountMinor);
        Assert.Equal(1_000, finalAllocatedMinor);
        Assert.Equal(1, persistedPayments);
        Assert.Equal(1, persistedAllocations);
    }

    [Fact]
    public async Task Concurrent_standalone_payment_and_customer_advance_cannot_overallocate_same_sale()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("cross-workflow-customer", UserRole.Responsable);
        var client = database.AddClient("Cross Workflow Customer");
        var sale = database.AddSale(user, client, 1_000);
        var sourcePayment = database.AddExistingPayment(user, client, 1_000);
        await database.AuthenticateAsync(user);

        using var standaloneScope = database.CreateIndependentScope();
        using var advanceScope = database.CreateIndependentScope();
        var standaloneContext = standaloneScope.ServiceProvider
            .GetRequiredService<FinancialManagementDbContext>();
        var advanceContext = advanceScope.ServiceProvider
            .GetRequiredService<FinancialManagementDbContext>();
        await standaloneContext.Database.OpenConnectionAsync();
        await advanceContext.Database.OpenConnectionAsync();
        Assert.NotSame(
            standaloneContext.Database.GetDbConnection(),
            advanceContext.Database.GetDbConnection());

        var standaloneService = standaloneScope.ServiceProvider
            .GetRequiredService<ICustomerPaymentService>();
        var advanceService = advanceScope.ServiceProvider
            .GetRequiredService<ICustomerAdvanceService>();
        var startGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var standaloneTask = Task.Run(async () =>
        {
            await startGate.Task;
            return await CaptureAttemptAsync(
                standaloneService,
                CreateCommand(client.Id, 1_000, SaleAllocation(sale.Id, 1_000)));
        });
        var advanceTask = Task.Run(async () =>
        {
            await startGate.Task;
            return await CaptureAdvanceAttemptAsync(
                advanceService,
                new ApplyCustomerAdvanceCommand(
                    sourcePayment.Id,
                    [new CustomerAdvanceAllocationInput(sale.Id, null, 1_000)]));
        });

        startGate.SetResult();
        var standaloneAttempt = await standaloneTask;
        var advanceAttempt = await advanceTask;
        standaloneContext.Database.CloseConnection();
        advanceContext.Database.CloseConnection();

        using var verificationScope = database.CreateIndependentScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<FinancialManagementDbContext>();
        var finalAllocatedMinor = await verificationContext.CustomerPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.SaleId == sale.Id)
            .SumAsync(allocation => allocation.AmountMinor);

        Assert.True(finalAllocatedMinor <= sale.TotalAmountMinor);
        var successfulWorkflows =
            (standaloneAttempt.Result?.Success == true ? 1 : 0)
            + (advanceAttempt.Result?.Success == true ? 1 : 0);
        Assert.Equal(
            successfulWorkflows,
            await verificationContext.CustomerPaymentAllocations
                .AsNoTracking()
                .CountAsync(allocation => allocation.SaleId == sale.Id));
        Assert.Equal(
            1 + (standaloneAttempt.Result?.Success == true ? 1 : 0),
            await verificationContext.CustomerPayments
                .AsNoTracking()
                .CountAsync(payment => payment.ClientId == client.Id));
    }

    [Fact]
    public async Task Customer_financial_workflow_remains_consistent_across_payment_sources()
    {
        using var database = new CustomerPaymentServiceTestDatabase();
        var user = database.AddUser("customer-chain", UserRole.Responsable);
        var client = database.AddClient("Customer Chain");
        await database.AuthenticateAsync(user);
        using var workflowScope = database.CreateIndependentScope();
        var advanceService = workflowScope.ServiceProvider
            .GetRequiredService<ICustomerAdvanceService>();
        var receivableService = workflowScope.ServiceProvider
            .GetRequiredService<IInitialCustomerReceivableService>();

        var saleResult = await database.SaleService.CreateAsync(new CreateSaleCommand(
            client.Id,
            1_000,
            null,
            null,
            new InitialCustomerPaymentInput(200, PaymentMethod.Cash, null)));
        Assert.True(saleResult.Success);

        var standaloneResult = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(client.Id, 500, SaleAllocation(saleResult.SaleId!.Value, 400)));
        Assert.True(standaloneResult.Success);

        var firstAdvance = await advanceService.ApplyAsync(new ApplyCustomerAdvanceCommand(
            standaloneResult.CustomerPaymentId!.Value,
            [new CustomerAdvanceAllocationInput(saleResult.SaleId.Value, null, 100)]));
        Assert.True(firstAdvance.Success);

        var receivableResult = await receivableService.CreateAsync(
            new CreateInitialCustomerReceivableCommand(
                client.Id,
                600,
                WorkflowLocalDate.AddDays(-30),
                null,
                null));
        Assert.True(receivableResult.Success);

        var secondPayment = await database.CustomerPaymentService.CreateAsync(
            CreateCommand(
                client.Id,
                700,
                SaleAllocation(saleResult.SaleId.Value, 300),
                ReceivableAllocation(receivableResult.InitialCustomerReceivableId!.Value, 300)));
        Assert.True(secondPayment.Success);

        var secondAdvance = await advanceService.ApplyAsync(new ApplyCustomerAdvanceCommand(
            secondPayment.CustomerPaymentId!.Value,
            [new CustomerAdvanceAllocationInput(
                null,
                receivableResult.InitialCustomerReceivableId.Value,
                100)]));
        Assert.True(secondAdvance.Success);

        using var verificationScope = database.CreateIndependentScope();
        var context = verificationScope.ServiceProvider
            .GetRequiredService<FinancialManagementDbContext>();
        Assert.Equal(
            1_000,
            await context.CustomerPaymentAllocations
                .Where(allocation => allocation.SaleId == saleResult.SaleId)
                .SumAsync(allocation => allocation.AmountMinor));
        Assert.Equal(
            400,
            await context.CustomerPaymentAllocations
                .Where(allocation => allocation.InitialCustomerReceivableId
                    == receivableResult.InitialCustomerReceivableId)
                .SumAsync(allocation => allocation.AmountMinor));
        var secondPaymentAllocated = await context.CustomerPaymentAllocations
            .Where(allocation => allocation.CustomerPaymentId == secondPayment.CustomerPaymentId)
            .SumAsync(allocation => allocation.AmountMinor);
        Assert.Equal(700, secondPaymentAllocated);
    }

    [Fact]
    public void Default_reference_generator_keeps_enc_format()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInfrastructure("Data Source=:memory:")
            .BuildServiceProvider();
        var generator = serviceProvider.GetRequiredService<IBusinessReferenceGenerator>();

        var reference = generator.GenerateCustomerPaymentReference(WorkflowLocalDate);

        Assert.Matches("^ENC-20260204-[0-9A-F]{12}$", reference);
    }

    private static async Task<ConcurrentPaymentAttempt> CaptureAttemptAsync(
        ICustomerPaymentService service,
        CreateCustomerPaymentCommand command)
    {
        try
        {
            return new ConcurrentPaymentAttempt(
                await service.CreateAsync(command),
                Exception: null);
        }
        catch (Exception exception)
        {
            return new ConcurrentPaymentAttempt(Result: null, exception);
        }
    }

    private static async Task<ConcurrentAdvanceAttempt> CaptureAdvanceAttemptAsync(
        ICustomerAdvanceService service,
        ApplyCustomerAdvanceCommand command)
    {
        try
        {
            return new ConcurrentAdvanceAttempt(await service.ApplyAsync(command), null);
        }
        catch (Exception exception)
        {
            return new ConcurrentAdvanceAttempt(null, exception);
        }
    }

    private static string DescribeAttempt(ConcurrentPaymentAttempt attempt)
    {
        if (attempt.Result?.Success == true)
        {
            return "success";
        }

        if (attempt.Result is not null)
        {
            return $"functional failure ({attempt.Result.FailureReason})";
        }

        return $"technical failure ({attempt.Exception!.GetType().Name})";
    }

    private static CreateCustomerPaymentCommand CreateCommand(
        long clientId,
        long amountMinor,
        params CustomerPaymentAllocationInput[] allocations) =>
        new(clientId, amountMinor, PaymentMethod.Cash, null, allocations);

    private static CustomerPaymentAllocationInput SaleAllocation(
        long saleId,
        long amountMinor) =>
        new(saleId, null, amountMinor);

    private static CustomerPaymentAllocationInput ReceivableAllocation(
        long receivableId,
        long amountMinor) =>
        new(null, receivableId, amountMinor);

    private static void AssertSuccessfulPayment(
        CustomerPaymentServiceTestDatabase database,
        CreateCustomerPaymentResult result,
        long clientId,
        long amountMinor,
        long allocatedAmountMinor,
        long advanceMinor,
        int allocationCount)
    {
        Assert.True(result.Success);
        Assert.Equal(CreateCustomerPaymentFailureReason.None, result.FailureReason);
        Assert.NotNull(result.CustomerPaymentId);
        Assert.NotNull(result.CustomerPaymentReference);
        Assert.Equal(amountMinor, result.AmountMinor);
        Assert.Equal(allocatedAmountMinor, result.AllocatedAmountMinor);
        Assert.Equal(advanceMinor, result.UnallocatedAdvanceMinor);
        Assert.Equal(allocationCount, result.AllocationCount);
        var payment = database.Context.CustomerPayments
            .Single(item => item.Id == result.CustomerPaymentId);
        Assert.Equal(clientId, payment.ClientId);
        var allocations = database.Context.CustomerPaymentAllocations
            .Where(allocation => allocation.CustomerPaymentId == result.CustomerPaymentId)
            .ToArray();
        Assert.Equal(allocationCount, allocations.Length);
        Assert.Equal(allocatedAmountMinor, allocations.Sum(item => item.AmountMinor));
    }

    private static void AssertFunctionalFailure(
        CustomerPaymentServiceTestDatabase database,
        CreateCustomerPaymentResult result,
        CreateCustomerPaymentFailureReason expectedFailure,
        int expectedPaymentCount = 0,
        int expectedAllocationCount = 0)
    {
        Assert.False(result.Success);
        Assert.Equal(expectedFailure, result.FailureReason);
        Assert.Null(result.CustomerPaymentId);
        Assert.Null(result.CustomerPaymentReference);
        Assert.Equal(0, result.AllocatedAmountMinor);
        Assert.Equal(0, result.UnallocatedAdvanceMinor);
        Assert.Equal(0, result.AllocationCount);
        Assert.Equal(expectedPaymentCount, database.Context.CustomerPayments.Count());
        Assert.Equal(
            expectedAllocationCount,
            database.Context.CustomerPaymentAllocations.Count());
    }

    private sealed class CustomerPaymentServiceTestDatabase : IDisposable
    {
        private const string Password = "Customer payment test password 42!";

        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _scope;
        private int _seedReferenceSequence;

        public CustomerPaymentServiceTestDatabase()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-CustomerPayments-{Guid.NewGuid():N}.db");
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
            CustomerPaymentService = _scope.ServiceProvider
                .GetRequiredService<ICustomerPaymentService>();
            SaleService = _scope.ServiceProvider.GetRequiredService<ISaleService>();
        }

        public FinancialManagementDbContext Context { get; }

        public ICustomerPaymentService CustomerPaymentService { get; }

        public ISaleService SaleService { get; }

        public IServiceScope CreateIndependentScope() =>
            _serviceProvider.CreateScope();

        private IPasswordHasher PasswordHasher { get; }

        private IAuthenticationService AuthenticationService { get; }

        public User AddUser(string username, UserRole role)
        {
            var user = new User
            {
                FullName = $"Customer Payment Test {username}",
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

        public Sale AddSale(User user, Client? client, long totalAmountMinor)
        {
            var sale = new Sale
            {
                Reference = $"VTE-EXISTING-{++_seedReferenceSequence:D4}",
                SaleDate = WorkflowLocalDate.AddDays(-1),
                TotalAmountMinor = totalAmountMinor,
                ClientId = client?.Id,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1),
                UpdatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.Sales.Add(sale);
            Context.SaveChanges();
            return sale;
        }

        public InitialCustomerReceivable AddReceivable(
            User user,
            Client client,
            long amountMinor)
        {
            var receivable = new InitialCustomerReceivable
            {
                Reference = $"ICR-EXISTING-{++_seedReferenceSequence:D4}",
                ClientId = client.Id,
                ReceivableDate = WorkflowLocalDate.AddDays(-1),
                InitialAmountMinor = amountMinor,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.InitialCustomerReceivables.Add(receivable);
            Context.SaveChanges();
            return receivable;
        }

        public CustomerPayment AddExistingPayment(
            User user,
            Client client,
            long amountMinor,
            params CustomerPaymentAllocationInput[] allocations)
        {
            var payment = new CustomerPayment
            {
                Reference = $"ENC-EXISTING-{++_seedReferenceSequence:D4}",
                ClientId = client.Id,
                PaymentDate = WorkflowLocalDate.AddDays(-1),
                AmountMinor = amountMinor,
                PaymentMethod = PaymentMethod.Cash,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.CustomerPayments.Add(payment);
            Context.SaveChanges();

            foreach (var allocation in allocations)
            {
                Context.CustomerPaymentAllocations.Add(new CustomerPaymentAllocation
                {
                    CustomerPaymentId = payment.Id,
                    SaleId = allocation.SaleId,
                    InitialCustomerReceivableId = allocation.InitialCustomerReceivableId,
                    AmountMinor = allocation.AmountMinor,
                    CreatedAtUtc = WorkflowUtc.AddDays(-1)
                });
            }

            Context.SaveChanges();
            return payment;
        }

        public void GrantCustomerPaymentsCreate(User employee)
        {
            Context.UserPermissions.Add(new UserPermission
            {
                UserId = employee.Id,
                Module = PermissionModule.CustomerPayments,
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
        private int _saleSequence;
        private int _customerPaymentSequence;

        public const string FirstCustomerPaymentReference =
            "ENC-20260204-000000000001";

        public string GenerateSaleReference(DateOnly businessDate) =>
            $"VTE-{businessDate:yyyyMMdd}-{Interlocked.Increment(ref _saleSequence):X12}";

        public string GenerateCustomerPaymentReference(DateOnly businessDate) =>
            $"ENC-{businessDate:yyyyMMdd}-" +
            $"{Interlocked.Increment(ref _customerPaymentSequence):X12}";

        public string GeneratePurchaseReference(DateOnly businessDate) =>
            "ACH-20260204-000000000001";

        public string GenerateSupplierPaymentReference(DateOnly businessDate) =>
            "DEC-20260204-000000000001";

        public string GenerateExpenseReference(DateOnly businessDate) =>
            $"DEP-{businessDate:yyyyMMdd}-000000000001";

        public string GenerateInitialCustomerReceivableReference(DateOnly historicalDate) =>
            $"CRE-{historicalDate:yyyyMMdd}-000000000001";

        public string GenerateInitialSupplierPayableReference(DateOnly historicalDate) =>
            $"DET-{historicalDate:yyyyMMdd}-000000000001";

        public string GenerateTreasuryTransferReference(DateOnly businessDate) =>
            $"TRF-{businessDate:yyyyMMdd}-000000000001";
    }

    private sealed record ConcurrentPaymentAttempt(
        CreateCustomerPaymentResult? Result,
        Exception? Exception);

    private sealed record ConcurrentAdvanceAttempt(
        ApplyCustomerAdvanceResult? Result,
        Exception? Exception);
}
