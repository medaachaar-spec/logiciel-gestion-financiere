using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.InitialSupplierPayables;
using FinancialManagement.Application.Purchases;
using FinancialManagement.Application.Sales;
using FinancialManagement.Application.SupplierAdvances;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class SupplierPaymentServiceTests
{
    private static readonly DateTimeOffset WorkflowUtc =
        new(2026, 2, 3, 23, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly WorkflowLocalDate = new(2026, 2, 4);

    [Theory]
    [InlineData(500L, true)]
    [InlineData(501L, false)]
    public async Task Purchase_outstanding_includes_multiple_existing_allocations(
        long requestedAmountMinor,
        bool shouldSucceed)
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser($"purchase-outstanding-{requestedAmountMinor}", UserRole.Responsable);
        var supplier = database.AddSupplier("Purchase Outstanding Supplier");
        var purchase = database.AddPurchase(user, supplier, 1_000);
        database.AddExistingPayment(
            user,
            supplier,
            500,
            PurchaseAllocation(purchase.Id, 200),
            PurchaseAllocation(purchase.Id, 300));
        await database.AuthenticateAsync(user);
        var paymentsBefore = database.Context.SupplierPayments.Count();
        var allocationsBefore = database.Context.SupplierPaymentAllocations.Count();

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(
                supplier.Id,
                requestedAmountMinor,
                PurchaseAllocation(purchase.Id, requestedAmountMinor)));

        if (shouldSucceed)
        {
            AssertSuccessfulPayment(database, result, supplier.Id, 500, 500, 0, 1);
            Assert.Equal(
                1_000,
                database.Context.SupplierPaymentAllocations
                    .Where(allocation => allocation.PurchaseId == purchase.Id)
                    .Sum(allocation => allocation.AmountMinor));
            Assert.Empty(database.Context.UserPermissions);
            return;
        }

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.AllocationExceedsOutstanding,
            paymentsBefore,
            allocationsBefore);
    }

    [Theory]
    [InlineData(500L, true)]
    [InlineData(501L, false)]
    public async Task Initial_payable_outstanding_includes_multiple_existing_allocations(
        long requestedAmountMinor,
        bool shouldSucceed)
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser($"payable-outstanding-{requestedAmountMinor}", UserRole.Responsable);
        var supplier = database.AddSupplier("Initial Payable Outstanding Supplier");
        var payable = database.AddPayable(user, supplier, 1_000);
        database.AddExistingPayment(
            user,
            supplier,
            500,
            PayableAllocation(payable.Id, 200),
            PayableAllocation(payable.Id, 300));
        await database.AuthenticateAsync(user);
        var paymentsBefore = database.Context.SupplierPayments.Count();
        var allocationsBefore = database.Context.SupplierPaymentAllocations.Count();

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(
                supplier.Id,
                requestedAmountMinor,
                PayableAllocation(payable.Id, requestedAmountMinor)));

        if (shouldSucceed)
        {
            Assert.True(result.Success);
            Assert.Equal(500, result.AllocatedAmountMinor);
            return;
        }

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.AllocationExceedsOutstanding,
            paymentsBefore,
            allocationsBefore);
    }

    [Fact]
    public async Task Payment_can_be_explicitly_split_across_two_purchases()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("two-purchases", UserRole.Responsable);
        var supplier = database.AddSupplier("Two Purchases Supplier");
        var firstPurchase = database.AddPurchase(user, supplier, 600);
        var secondPurchase = database.AddPurchase(user, supplier, 500);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(
                supplier.Id,
                800,
                PurchaseAllocation(firstPurchase.Id, 600),
                PurchaseAllocation(secondPurchase.Id, 150)));

        AssertSuccessfulPayment(database, result, supplier.Id, 800, 750, 50, 2);
        Assert.Equal(
            600,
            database.Context.SupplierPaymentAllocations
                .Single(item => item.PurchaseId == firstPurchase.Id).AmountMinor);
        Assert.Equal(
            150,
            database.Context.SupplierPaymentAllocations
                .Single(item => item.PurchaseId == secondPurchase.Id).AmountMinor);
    }

    [Fact]
    public async Task Payment_can_target_purchase_and_initial_payable()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("mixed-targets", UserRole.Responsable);
        var supplier = database.AddSupplier("Mixed Targets Supplier");
        var purchase = database.AddPurchase(user, supplier, 600);
        var payable = database.AddPayable(user, supplier, 500);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(
                supplier.Id,
                800,
                PurchaseAllocation(purchase.Id, 600),
                PayableAllocation(payable.Id, 200)));

        AssertSuccessfulPayment(database, result, supplier.Id, 800, 800, 0, 2);
        Assert.Contains(
            database.Context.SupplierPaymentAllocations,
            allocation => allocation.PurchaseId == purchase.Id
                && allocation.InitialSupplierPayableId is null);
        Assert.Contains(
            database.Context.SupplierPaymentAllocations,
            allocation => allocation.PurchaseId is null
                && allocation.InitialSupplierPayableId == payable.Id);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Fully_allocated_obligation_rejects_another_allocation(bool targetPurchase)
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser($"fully-allocated-{targetPurchase}", UserRole.Responsable);
        var supplier = database.AddSupplier("Fully Allocated Supplier");
        var target = targetPurchase
            ? PurchaseAllocation(database.AddPurchase(user, supplier, 500).Id, 500)
            : PayableAllocation(database.AddPayable(user, supplier, 500).Id, 500);
        database.AddExistingPayment(user, supplier, 500, target);
        await database.AuthenticateAsync(user);
        var requested = targetPurchase
            ? PurchaseAllocation(target.PurchaseId!.Value, 1)
            : PayableAllocation(target.InitialSupplierPayableId!.Value, 1);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(supplier.Id, 1, requested));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.AllocationExceedsOutstanding,
            expectedPaymentCount: 1,
            expectedAllocationCount: 1);
    }

    [Fact]
    public async Task Outstanding_includes_initial_payment_created_by_purchase_core()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("purchase-core-payment", UserRole.Responsable);
        var supplier = database.AddSupplier("Purchase Core Supplier");
        await database.AuthenticateAsync(user);
        var purchaseResult = await database.PurchaseService.CreateAsync(new CreatePurchaseCommand(
            supplier.Id,
            1_000,
            null,
            null,
            new InitialSupplierPaymentInput(400, PaymentMethod.Cash, null)));

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(
                supplier.Id,
                600,
                PurchaseAllocation(purchaseResult.PurchaseId!.Value, 600)));

        Assert.True(result.Success);
        Assert.Equal(2, database.Context.SupplierPayments.Count());
        Assert.Equal(
            1_000,
            database.Context.SupplierPaymentAllocations
                .Where(allocation => allocation.PurchaseId == purchaseResult.PurchaseId)
                .Sum(allocation => allocation.AmountMinor));
    }

    [Fact]
    public async Task Allocations_exceeding_payment_are_rejected_without_writes()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("sum-exceeds-payment", UserRole.Responsable);
        var supplier = database.AddSupplier("Allocation Sum Supplier");
        var firstPurchase = database.AddPurchase(user, supplier, 700);
        var secondPurchase = database.AddPurchase(user, supplier, 500);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(
                supplier.Id,
                1_000,
                PurchaseAllocation(firstPurchase.Id, 700),
                PurchaseAllocation(secondPurchase.Id, 400)));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.AllocationsExceedPayment);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Obligation_from_another_supplier_is_rejected(bool targetPurchase)
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser($"different-supplier-{targetPurchase}", UserRole.Responsable);
        var payingSupplier = database.AddSupplier("Paying Supplier");
        var otherSupplier = database.AddSupplier("Other Supplier");
        var input = targetPurchase
            ? PurchaseAllocation(database.AddPurchase(user, otherSupplier, 500).Id, 100)
            : PayableAllocation(database.AddPayable(user, otherSupplier, 500).Id, 100);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(payingSupplier.Id, 100, input));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.ObligationBelongsToDifferentSupplier);
    }

    [Theory]
    [InlineData(-1L)]
    [InlineData(0L)]
    [InlineData(999_999L)]
    public async Task Invalid_or_unknown_supplier_is_rejected(long supplierId)
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser($"invalid-supplier-{supplierId}", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(supplierId, 100, PurchaseAllocation(1, 100)));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.InvalidSupplier);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    public async Task Non_positive_payment_is_rejected(long amountMinor)
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser($"invalid-payment-{amountMinor}", UserRole.Responsable);
        var supplier = database.AddSupplier("Invalid Payment Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(supplier.Id, amountMinor, PurchaseAllocation(1, 1)));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.InvalidAmount);
    }

    [Fact]
    public async Task Undefined_payment_method_is_rejected()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("invalid-method", UserRole.Responsable);
        var supplier = database.AddSupplier("Invalid Method Supplier");
        await database.AuthenticateAsync(user);
        var command = new CreateSupplierPaymentCommand(
            supplier.Id,
            100,
            (PaymentMethod)999,
            null,
            [PurchaseAllocation(1, 100)]);

        var result = await database.SupplierPaymentService.CreateAsync(command);

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.InvalidPaymentMethod);
    }

    [Theory]
    [MemberData(nameof(MissingAllocationCollections))]
    public async Task Missing_allocations_are_rejected(
        IReadOnlyCollection<SupplierPaymentAllocationInput>? allocations)
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("missing-allocations", UserRole.Responsable);
        var supplier = database.AddSupplier("Missing Allocations Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            new CreateSupplierPaymentCommand(
                supplier.Id,
                100,
                PaymentMethod.Cash,
                null,
                allocations));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.MissingAllocations);
    }

    public static TheoryData<IReadOnlyCollection<SupplierPaymentAllocationInput>?>
        MissingAllocationCollections =>
        new()
        {
            null,
            Array.Empty<SupplierPaymentAllocationInput>()
        };

    [Theory]
    [MemberData(nameof(InvalidAllocations))]
    public async Task Invalid_allocation_structure_is_rejected(
        SupplierPaymentAllocationInput allocation)
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser($"invalid-allocation-{Guid.NewGuid():N}", UserRole.Responsable);
        var supplier = database.AddSupplier("Invalid Allocation Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(supplier.Id, 100, allocation));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.InvalidAllocation);
    }

    public static TheoryData<SupplierPaymentAllocationInput> InvalidAllocations =>
        new()
        {
            new SupplierPaymentAllocationInput(null, null, 100),
            new SupplierPaymentAllocationInput(1, 1, 100),
            new SupplierPaymentAllocationInput(1, null, 0),
            new SupplierPaymentAllocationInput(1, null, -1),
            new SupplierPaymentAllocationInput(0, null, 100),
            new SupplierPaymentAllocationInput(null, -1, 100)
        };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Duplicate_allocation_target_is_rejected(bool targetPurchase)
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser($"duplicate-{targetPurchase}", UserRole.Responsable);
        var supplier = database.AddSupplier("Duplicate Target Supplier");
        var first = targetPurchase ? PurchaseAllocation(1, 50) : PayableAllocation(1, 50);
        var second = targetPurchase ? PurchaseAllocation(1, 50) : PayableAllocation(1, 50);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(supplier.Id, 100, first, second));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.DuplicateAllocationTarget);
    }

    [Fact]
    public async Task Missing_obligation_is_rejected()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("missing-obligation", UserRole.Responsable);
        var supplier = database.AddSupplier("Missing Obligation Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(supplier.Id, 100, PurchaseAllocation(999_999, 100)));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.ObligationNotFound);
    }

    [Fact]
    public async Task Unauthenticated_user_is_rejected()
    {
        using var database = new SupplierPaymentServiceTestDatabase();

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(1, 100, PurchaseAllocation(1, 100)));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.NotAuthenticated);
    }

    [Fact]
    public async Task Employee_without_supplier_payment_create_is_rejected()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var employee = database.AddUser("employee-forbidden", UserRole.Employe);
        var supplier = database.AddSupplier("Forbidden Supplier");
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(supplier.Id, 100, PurchaseAllocation(1, 100)));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.Forbidden);
    }

    [Fact]
    public async Task Employee_with_supplier_payment_create_does_not_need_purchase_create()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var employee = database.AddUser("employee-allowed", UserRole.Employe);
        var supplier = database.AddSupplier("Allowed Supplier");
        var purchase = database.AddPurchase(employee, supplier, 100);
        database.GrantSupplierPaymentsCreate(employee);
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(supplier.Id, 100, PurchaseAllocation(purchase.Id, 100)));

        Assert.True(result.Success);
        Assert.DoesNotContain(
            database.Context.UserPermissions,
            item => item.Module == PermissionModule.Purchases);
    }

    [Fact]
    public async Task Payment_metadata_uses_session_time_provider_and_deterministic_reference()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("metadata", UserRole.Responsable);
        var supplier = database.AddSupplier("Metadata Supplier");
        var purchase = database.AddPurchase(user, supplier, 500);
        await database.AuthenticateAsync(user);
        var command = new CreateSupplierPaymentCommand(
            supplier.Id,
            500,
            PaymentMethod.BankTransfer,
            "Supplier payment observation",
            [PurchaseAllocation(purchase.Id, 500)]);

        var result = await database.SupplierPaymentService.CreateAsync(command);

        Assert.True(result.Success);
        Assert.Equal(DeterministicReferenceGenerator.FirstSupplierPaymentReference, result.SupplierPaymentReference);
        var payment = database.Context.SupplierPayments.Single(item => item.Id == result.SupplierPaymentId);
        Assert.Equal(user.Id, payment.CreatedByUserId);
        Assert.Equal(supplier.Id, payment.SupplierId);
        Assert.Equal(WorkflowLocalDate, payment.PaymentDate);
        Assert.Equal(WorkflowUtc, payment.CreatedAtUtc);
        Assert.Equal(PaymentMethod.BankTransfer, payment.PaymentMethod);
        Assert.Equal("Supplier payment observation", payment.Observation);
        Assert.All(
            database.Context.SupplierPaymentAllocations,
            allocation => Assert.Equal(WorkflowUtc, allocation.CreatedAtUtc));
    }

    [Fact]
    public async Task Unique_reference_collision_rolls_back_payment_and_allocations()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("reference-collision", UserRole.Responsable);
        var supplier = database.AddSupplier("Collision Supplier");
        var purchase = database.AddPurchase(user, supplier, 500);
        database.AddExistingPayment(
            user,
            supplier,
            100,
            reference: DeterministicReferenceGenerator.FirstSupplierPaymentReference);
        await database.AuthenticateAsync(user);

        await Assert.ThrowsAsync<DbUpdateException>(() =>
            database.SupplierPaymentService.CreateAsync(
                CreateCommand(supplier.Id, 500, PurchaseAllocation(purchase.Id, 500))));

        database.Context.ChangeTracker.Clear();
        Assert.Single(database.Context.SupplierPayments);
        Assert.Empty(database.Context.SupplierPaymentAllocations);
    }

    [Fact]
    public async Task Existing_advance_and_untargeted_obligation_remain_unchanged()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("existing-advance", UserRole.Responsable);
        var supplier = database.AddSupplier("Existing Advance Supplier");
        var oldTarget = database.AddPurchase(user, supplier, 200);
        var requestedTarget = database.AddPurchase(user, supplier, 300);
        var untouchedTarget = database.AddPurchase(user, supplier, 400);
        var existingPayment = database.AddExistingPayment(
            user,
            supplier,
            1_000,
            PurchaseAllocation(oldTarget.Id, 200));
        var existingAllocationId = database.Context.SupplierPaymentAllocations.Single().Id;
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(supplier.Id, 300, PurchaseAllocation(requestedTarget.Id, 300)));

        Assert.True(result.Success);
        Assert.Equal(
            1_000,
            database.Context.SupplierPayments.AsNoTracking()
                .Single(item => item.Id == existingPayment.Id).AmountMinor);
        Assert.Equal(
            200,
            database.Context.SupplierPaymentAllocations.AsNoTracking()
                .Single(item => item.Id == existingAllocationId).AmountMinor);
        Assert.DoesNotContain(
            database.Context.SupplierPaymentAllocations,
            allocation => allocation.PurchaseId == untouchedTarget.Id);
        Assert.Single(
            database.Context.SupplierPaymentAllocations,
            allocation => allocation.SupplierPaymentId == result.SupplierPaymentId);
    }

    [Fact]
    public async Task Corrupted_existing_overallocation_raises_technical_error()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("corrupted-overallocation", UserRole.Responsable);
        var supplier = database.AddSupplier("Corrupted Supplier");
        var purchase = database.AddPurchase(user, supplier, 1_000);
        database.AddExistingPayment(
            user,
            supplier,
            1_200,
            PurchaseAllocation(purchase.Id, 1_200));
        await database.AuthenticateAsync(user);
        var paymentsBefore = database.Context.SupplierPayments.Count();
        var allocationsBefore = database.Context.SupplierPaymentAllocations.Count();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.SupplierPaymentService.CreateAsync(
                CreateCommand(supplier.Id, 100, PurchaseAllocation(purchase.Id, 100))));

        Assert.Equal(paymentsBefore, database.Context.SupplierPayments.Count());
        Assert.Equal(allocationsBefore, database.Context.SupplierPaymentAllocations.Count());
    }

    [Fact]
    public async Task Allocation_sum_validation_does_not_overflow_long()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("long-overflow", UserRole.Responsable);
        var supplier = database.AddSupplier("Long Overflow Supplier");
        await database.AuthenticateAsync(user);

        var result = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(
                supplier.Id,
                long.MaxValue,
                PurchaseAllocation(1, long.MaxValue - 1),
                PurchaseAllocation(2, 2)));

        AssertFunctionalFailure(
            database,
            result,
            CreateSupplierPaymentFailureReason.AllocationsExceedPayment);
    }

    [Fact]
    public async Task Cancellation_is_propagated_without_writes()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("cancelled", UserRole.Responsable);
        var supplier = database.AddSupplier("Cancelled Supplier");
        await database.AuthenticateAsync(user);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SupplierPaymentService.CreateAsync(
                CreateCommand(supplier.Id, 100, PurchaseAllocation(1, 100)),
                cancellation.Token));

        Assert.Empty(database.Context.SupplierPayments);
        Assert.Empty(database.Context.SupplierPaymentAllocations);
    }

    [Fact]
    public async Task Concurrent_payments_cannot_silently_overallocate_same_purchase()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("concurrent-payments", UserRole.Responsable);
        var supplier = database.AddSupplier("Concurrent Supplier");
        var purchase = database.AddPurchase(user, supplier, 1_000);
        await database.AuthenticateAsync(user);
        using var firstScope = database.CreateIndependentScope();
        using var secondScope = database.CreateIndependentScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        await firstContext.Database.OpenConnectionAsync();
        await secondContext.Database.OpenConnectionAsync();
        Assert.NotSame(firstContext.Database.GetDbConnection(), secondContext.Database.GetDbConnection());
        var firstService = firstScope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();
        var command = CreateCommand(supplier.Id, 1_000, PurchaseAllocation(purchase.Id, 1_000));
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
        Console.WriteLine($"Supplier workflow A: {DescribeAttempt(attempts[0])}");
        Console.WriteLine($"Supplier workflow B: {DescribeAttempt(attempts[1])}");

        firstContext.Database.CloseConnection();
        secondContext.Database.CloseConnection();
        using var verificationScope = database.CreateIndependentScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<FinancialManagementDbContext>();
        var finalAllocatedMinor = await verificationContext.SupplierPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.PurchaseId == purchase.Id)
            .SumAsync(allocation => allocation.AmountMinor);

        Assert.True(finalAllocatedMinor <= purchase.TotalAmountMinor);
        Assert.Equal(1_000, finalAllocatedMinor);
        Assert.Equal(
            1,
            await verificationContext.SupplierPayments.AsNoTracking()
                .CountAsync(payment => payment.SupplierId == supplier.Id));
        Assert.Equal(
            1,
            await verificationContext.SupplierPaymentAllocations.AsNoTracking()
                .CountAsync(allocation => allocation.PurchaseId == purchase.Id));
    }

    [Fact]
    public async Task Concurrent_standalone_payment_and_supplier_advance_cannot_overallocate_same_purchase()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("cross-workflow-supplier", UserRole.Responsable);
        var supplier = database.AddSupplier("Cross Workflow Supplier");
        var purchase = database.AddPurchase(user, supplier, 1_000);
        var sourcePayment = database.AddExistingPayment(user, supplier, 1_000);
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
            .GetRequiredService<ISupplierPaymentService>();
        var advanceService = advanceScope.ServiceProvider
            .GetRequiredService<ISupplierAdvanceService>();
        var startGate = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var standaloneTask = Task.Run(async () =>
        {
            await startGate.Task;
            return await CaptureAttemptAsync(
                standaloneService,
                CreateCommand(supplier.Id, 1_000, PurchaseAllocation(purchase.Id, 1_000)));
        });
        var advanceTask = Task.Run(async () =>
        {
            await startGate.Task;
            return await CaptureAdvanceAttemptAsync(
                advanceService,
                new ApplySupplierAdvanceCommand(
                    sourcePayment.Id,
                    [new SupplierAdvanceAllocationInput(purchase.Id, null, 1_000)]));
        });

        startGate.SetResult();
        var standaloneAttempt = await standaloneTask;
        var advanceAttempt = await advanceTask;
        standaloneContext.Database.CloseConnection();
        advanceContext.Database.CloseConnection();

        using var verificationScope = database.CreateIndependentScope();
        var verificationContext = verificationScope.ServiceProvider
            .GetRequiredService<FinancialManagementDbContext>();
        var finalAllocatedMinor = await verificationContext.SupplierPaymentAllocations
            .AsNoTracking()
            .Where(allocation => allocation.PurchaseId == purchase.Id)
            .SumAsync(allocation => allocation.AmountMinor);

        Assert.True(finalAllocatedMinor <= purchase.TotalAmountMinor);
        var successfulWorkflows =
            (standaloneAttempt.Result?.Success == true ? 1 : 0)
            + (advanceAttempt.Result?.Success == true ? 1 : 0);
        Assert.Equal(
            successfulWorkflows,
            await verificationContext.SupplierPaymentAllocations
                .AsNoTracking()
                .CountAsync(allocation => allocation.PurchaseId == purchase.Id));
        Assert.Equal(
            1 + (standaloneAttempt.Result?.Success == true ? 1 : 0),
            await verificationContext.SupplierPayments
                .AsNoTracking()
                .CountAsync(payment => payment.SupplierId == supplier.Id));
    }

    [Fact]
    public async Task Supplier_financial_workflow_remains_consistent_across_payment_sources()
    {
        using var database = new SupplierPaymentServiceTestDatabase();
        var user = database.AddUser("supplier-chain", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier Chain");
        await database.AuthenticateAsync(user);
        using var workflowScope = database.CreateIndependentScope();
        var advanceService = workflowScope.ServiceProvider
            .GetRequiredService<ISupplierAdvanceService>();
        var payableService = workflowScope.ServiceProvider
            .GetRequiredService<IInitialSupplierPayableService>();

        var purchaseResult = await database.PurchaseService.CreateAsync(new CreatePurchaseCommand(
            supplier.Id,
            1_000,
            null,
            null,
            new InitialSupplierPaymentInput(200, PaymentMethod.Cash, null)));
        Assert.True(purchaseResult.Success);

        var standaloneResult = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(supplier.Id, 500, PurchaseAllocation(purchaseResult.PurchaseId!.Value, 400)));
        Assert.True(standaloneResult.Success);

        var firstAdvance = await advanceService.ApplyAsync(new ApplySupplierAdvanceCommand(
            standaloneResult.SupplierPaymentId!.Value,
            [new SupplierAdvanceAllocationInput(purchaseResult.PurchaseId.Value, null, 100)]));
        Assert.True(firstAdvance.Success);

        var payableResult = await payableService.CreateAsync(
            new CreateInitialSupplierPayableCommand(
                supplier.Id,
                600,
                WorkflowLocalDate.AddDays(-30),
                null,
                null));
        Assert.True(payableResult.Success);

        var secondPayment = await database.SupplierPaymentService.CreateAsync(
            CreateCommand(
                supplier.Id,
                700,
                PurchaseAllocation(purchaseResult.PurchaseId.Value, 300),
                PayableAllocation(payableResult.InitialSupplierPayableId!.Value, 300)));
        Assert.True(secondPayment.Success);

        var secondAdvance = await advanceService.ApplyAsync(new ApplySupplierAdvanceCommand(
            secondPayment.SupplierPaymentId!.Value,
            [new SupplierAdvanceAllocationInput(
                null,
                payableResult.InitialSupplierPayableId.Value,
                100)]));
        Assert.True(secondAdvance.Success);

        using var verificationScope = database.CreateIndependentScope();
        var context = verificationScope.ServiceProvider
            .GetRequiredService<FinancialManagementDbContext>();
        Assert.Equal(
            1_000,
            await context.SupplierPaymentAllocations
                .Where(allocation => allocation.PurchaseId == purchaseResult.PurchaseId)
                .SumAsync(allocation => allocation.AmountMinor));
        Assert.Equal(
            400,
            await context.SupplierPaymentAllocations
                .Where(allocation => allocation.InitialSupplierPayableId
                    == payableResult.InitialSupplierPayableId)
                .SumAsync(allocation => allocation.AmountMinor));
        var secondPaymentAllocated = await context.SupplierPaymentAllocations
            .Where(allocation => allocation.SupplierPaymentId == secondPayment.SupplierPaymentId)
            .SumAsync(allocation => allocation.AmountMinor);
        Assert.Equal(700, secondPaymentAllocated);
    }

    [Fact]
    public void Default_reference_generator_keeps_dec_format()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInfrastructure("Data Source=:memory:")
            .BuildServiceProvider();
        var generator = serviceProvider.GetRequiredService<IBusinessReferenceGenerator>();

        var reference = generator.GenerateSupplierPaymentReference(WorkflowLocalDate);

        Assert.Matches("^DEC-20260204-[0-9A-F]{12}$", reference);
    }

    private static async Task<ConcurrentPaymentAttempt> CaptureAttemptAsync(
        ISupplierPaymentService service,
        CreateSupplierPaymentCommand command)
    {
        try
        {
            return new ConcurrentPaymentAttempt(await service.CreateAsync(command), Exception: null);
        }
        catch (Exception exception)
        {
            return new ConcurrentPaymentAttempt(Result: null, exception);
        }
    }

    private static async Task<ConcurrentAdvanceAttempt> CaptureAdvanceAttemptAsync(
        ISupplierAdvanceService service,
        ApplySupplierAdvanceCommand command)
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

    private static CreateSupplierPaymentCommand CreateCommand(
        long supplierId,
        long amountMinor,
        params SupplierPaymentAllocationInput[] allocations) =>
        new(supplierId, amountMinor, PaymentMethod.Cash, null, allocations);

    private static SupplierPaymentAllocationInput PurchaseAllocation(
        long purchaseId,
        long amountMinor) =>
        new(purchaseId, null, amountMinor);

    private static SupplierPaymentAllocationInput PayableAllocation(
        long payableId,
        long amountMinor) =>
        new(null, payableId, amountMinor);

    private static void AssertSuccessfulPayment(
        SupplierPaymentServiceTestDatabase database,
        CreateSupplierPaymentResult result,
        long supplierId,
        long amountMinor,
        long allocatedAmountMinor,
        long advanceMinor,
        int allocationCount)
    {
        Assert.True(result.Success);
        Assert.Equal(CreateSupplierPaymentFailureReason.None, result.FailureReason);
        Assert.NotNull(result.SupplierPaymentId);
        Assert.NotNull(result.SupplierPaymentReference);
        Assert.Equal(amountMinor, result.AmountMinor);
        Assert.Equal(allocatedAmountMinor, result.AllocatedAmountMinor);
        Assert.Equal(advanceMinor, result.UnallocatedSupplierAdvanceMinor);
        Assert.Equal(allocationCount, result.AllocationCount);
        var payment = database.Context.SupplierPayments.Single(item => item.Id == result.SupplierPaymentId);
        Assert.Equal(supplierId, payment.SupplierId);
        var allocations = database.Context.SupplierPaymentAllocations
            .Where(allocation => allocation.SupplierPaymentId == result.SupplierPaymentId)
            .ToArray();
        Assert.Equal(allocationCount, allocations.Length);
        Assert.Equal(allocatedAmountMinor, allocations.Sum(item => item.AmountMinor));
    }

    private static void AssertFunctionalFailure(
        SupplierPaymentServiceTestDatabase database,
        CreateSupplierPaymentResult result,
        CreateSupplierPaymentFailureReason expectedFailure,
        int expectedPaymentCount = 0,
        int expectedAllocationCount = 0)
    {
        Assert.False(result.Success);
        Assert.Equal(expectedFailure, result.FailureReason);
        Assert.Null(result.SupplierPaymentId);
        Assert.Null(result.SupplierPaymentReference);
        Assert.Equal(0, result.AllocatedAmountMinor);
        Assert.Equal(0, result.UnallocatedSupplierAdvanceMinor);
        Assert.Equal(0, result.AllocationCount);
        Assert.Equal(expectedPaymentCount, database.Context.SupplierPayments.Count());
        Assert.Equal(expectedAllocationCount, database.Context.SupplierPaymentAllocations.Count());
    }

    private sealed class SupplierPaymentServiceTestDatabase : IDisposable
    {
        private const string Password = "Supplier payment test password 42!";

        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _scope;
        private int _seedReferenceSequence;

        public SupplierPaymentServiceTestDatabase()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-SupplierPayments-{Guid.NewGuid():N}.db");
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
            SupplierPaymentService = _scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();
            PurchaseService = _scope.ServiceProvider.GetRequiredService<IPurchaseService>();
        }

        public FinancialManagementDbContext Context { get; }

        public ISupplierPaymentService SupplierPaymentService { get; }

        public IPurchaseService PurchaseService { get; }

        public IServiceScope CreateIndependentScope() => _serviceProvider.CreateScope();

        private IPasswordHasher PasswordHasher { get; }

        private IAuthenticationService AuthenticationService { get; }

        public User AddUser(string username, UserRole role)
        {
            var user = new User
            {
                FullName = $"Supplier Payment Test {username}",
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

        public Purchase AddPurchase(User user, Supplier supplier, long totalAmountMinor)
        {
            var purchase = new Purchase
            {
                Reference = $"ACH-EXISTING-{++_seedReferenceSequence:D4}",
                PurchaseDate = WorkflowLocalDate.AddDays(-1),
                TotalAmountMinor = totalAmountMinor,
                SupplierId = supplier.Id,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1),
                UpdatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.Purchases.Add(purchase);
            Context.SaveChanges();
            return purchase;
        }

        public InitialSupplierPayable AddPayable(User user, Supplier supplier, long amountMinor)
        {
            var payable = new InitialSupplierPayable
            {
                Reference = $"ISP-EXISTING-{++_seedReferenceSequence:D4}",
                SupplierId = supplier.Id,
                PayableDate = WorkflowLocalDate.AddDays(-1),
                InitialAmountMinor = amountMinor,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.InitialSupplierPayables.Add(payable);
            Context.SaveChanges();
            return payable;
        }

        public SupplierPayment AddExistingPayment(
            User user,
            Supplier supplier,
            long amountMinor,
            params SupplierPaymentAllocationInput[] allocations) =>
            AddExistingPayment(user, supplier, amountMinor, reference: null, allocations);

        public SupplierPayment AddExistingPayment(
            User user,
            Supplier supplier,
            long amountMinor,
            string? reference,
            params SupplierPaymentAllocationInput[] allocations)
        {
            var payment = new SupplierPayment
            {
                Reference = reference ?? $"DEC-EXISTING-{++_seedReferenceSequence:D4}",
                SupplierId = supplier.Id,
                PaymentDate = WorkflowLocalDate.AddDays(-1),
                AmountMinor = amountMinor,
                PaymentMethod = PaymentMethod.Cash,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.SupplierPayments.Add(payment);
            Context.SaveChanges();

            foreach (var allocation in allocations)
            {
                Context.SupplierPaymentAllocations.Add(new SupplierPaymentAllocation
                {
                    SupplierPaymentId = payment.Id,
                    PurchaseId = allocation.PurchaseId,
                    InitialSupplierPayableId = allocation.InitialSupplierPayableId,
                    AmountMinor = allocation.AmountMinor,
                    CreatedAtUtc = WorkflowUtc.AddDays(-1)
                });
            }

            Context.SaveChanges();
            return payment;
        }

        public void GrantSupplierPaymentsCreate(User employee)
        {
            Context.UserPermissions.Add(new UserPermission
            {
                UserId = employee.Id,
                Module = PermissionModule.SupplierPayments,
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
            "FinancialManagement-Supplier-Test-UTC-02",
            TimeSpan.FromHours(2),
            "FinancialManagement Supplier Test UTC+02",
            "FinancialManagement Supplier Test UTC+02");

        public override TimeZoneInfo LocalTimeZone => TestTimeZone;

        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class DeterministicReferenceGenerator : IBusinessReferenceGenerator
    {
        private int _purchaseSequence;
        private int _supplierPaymentSequence;

        public const string FirstSupplierPaymentReference =
            "DEC-20260204-000000000001";

        public string GenerateSaleReference(DateOnly businessDate) =>
            "VTE-20260204-000000000001";

        public string GenerateCustomerPaymentReference(DateOnly businessDate) =>
            "ENC-20260204-000000000001";

        public string GeneratePurchaseReference(DateOnly businessDate) =>
            $"ACH-{businessDate:yyyyMMdd}-{Interlocked.Increment(ref _purchaseSequence):X12}";

        public string GenerateSupplierPaymentReference(DateOnly businessDate) =>
            $"DEC-{businessDate:yyyyMMdd}-" +
            $"{Interlocked.Increment(ref _supplierPaymentSequence):X12}";

        public string GenerateInitialCustomerReceivableReference(DateOnly historicalDate) =>
            $"CRE-{historicalDate:yyyyMMdd}-000000000001";

        public string GenerateInitialSupplierPayableReference(DateOnly historicalDate) =>
            $"DET-{historicalDate:yyyyMMdd}-000000000001";
    }

    private sealed record ConcurrentPaymentAttempt(
        CreateSupplierPaymentResult? Result,
        Exception? Exception);

    private sealed record ConcurrentAdvanceAttempt(
        ApplySupplierAdvanceResult? Result,
        Exception? Exception);
}
