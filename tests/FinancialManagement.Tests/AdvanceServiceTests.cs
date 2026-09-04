using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Application.SupplierAdvances;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class AdvanceServiceTests
{
    private static readonly DateTimeOffset WorkflowUtc =
        new(2026, 2, 3, 23, 30, 0, TimeSpan.Zero);

    private static readonly DateOnly BusinessDate = new(2026, 2, 4);

    [Fact]
    public async Task Customer_advance_can_be_partially_then_fully_applied()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-partial", UserRole.Responsable);
        var client = database.AddClient("Customer Partial Advance");
        var oldTarget = database.AddSale(user, client, 1_000);
        var newTarget = database.AddSale(user, client, 200);
        var source = database.AddCustomerPayment(
            user,
            client,
            1_200,
            CustomerSaleAllocation(oldTarget.Id, 1_000));
        var sourceSnapshot = SourceSnapshot(source);
        var oldAllocation = database.Context.CustomerPaymentAllocations.Single();
        await database.AuthenticateAsync(user);

        var first = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(source.Id, CustomerSaleAllocation(newTarget.Id, 150)));
        var second = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(source.Id, CustomerSaleAllocation(newTarget.Id, 50)));
        var third = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(source.Id, CustomerSaleAllocation(newTarget.Id, 1)));

        AssertCustomerSuccess(first, source, 200, 150, 50, 1);
        AssertCustomerSuccess(second, source, 50, 50, 0, 1);
        Assert.False(third.Success);
        Assert.Equal(ApplyCustomerAdvanceFailureReason.NoAvailableAdvance, third.FailureReason);
        Assert.Equal(1_200, database.Context.CustomerPaymentAllocations
            .Where(item => item.CustomerPaymentId == source.Id)
            .Sum(item => item.AmountMinor));
        Assert.Equal(1_000, database.Context.CustomerPaymentAllocations
            .Single(item => item.Id == oldAllocation.Id).AmountMinor);
        AssertSourceUnchanged(sourceSnapshot, database.Context.CustomerPayments.Single());
        Assert.Single(database.Context.CustomerPayments);
        Assert.Empty(database.Context.TreasuryOpeningBalances);
        Assert.Empty(database.Context.TreasuryTransfers);
        Assert.Empty(database.Context.UserPermissions);
        Assert.All(
            database.Context.CustomerPaymentAllocations.Where(item => item.Id != oldAllocation.Id),
            item => Assert.Equal(WorkflowUtc, item.CreatedAtUtc));
    }

    [Fact]
    public async Task Customer_advance_can_be_split_across_two_sales_without_touching_other_target()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-two-sales", UserRole.Responsable);
        var client = database.AddClient("Customer Two Sales");
        var firstSale = database.AddSale(user, client, 300);
        var secondSale = database.AddSale(user, client, 200);
        var untouched = database.AddSale(user, client, 400);
        var source = database.AddCustomerPayment(user, client, 500);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(
                source.Id,
                CustomerSaleAllocation(firstSale.Id, 300),
                CustomerSaleAllocation(secondSale.Id, 150)));

        AssertCustomerSuccess(result, source, 500, 450, 50, 2);
        Assert.DoesNotContain(
            database.Context.CustomerPaymentAllocations,
            item => item.SaleId == untouched.Id);
    }

    [Fact]
    public async Task Customer_advance_can_target_sale_and_initial_receivable()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-mixed", UserRole.Responsable);
        var client = database.AddClient("Customer Mixed Targets");
        var sale = database.AddSale(user, client, 300);
        var receivable = database.AddReceivable(user, client, 200);
        var source = database.AddCustomerPayment(user, client, 500);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(
                source.Id,
                CustomerSaleAllocation(sale.Id, 300),
                CustomerReceivableAllocation(receivable.Id, 150)));

        AssertCustomerSuccess(result, source, 500, 450, 50, 2);
        Assert.Contains(database.Context.CustomerPaymentAllocations, item => item.SaleId == sale.Id);
        Assert.Contains(
            database.Context.CustomerPaymentAllocations,
            item => item.InitialCustomerReceivableId == receivable.Id);
    }

    [Fact]
    public async Task Customer_allocations_exceeding_available_advance_are_rejected()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-advance-limit", UserRole.Responsable);
        var client = database.AddClient("Customer Advance Limit");
        var oldTarget = database.AddSale(user, client, 800);
        var newTarget = database.AddSale(user, client, 300);
        var source = database.AddCustomerPayment(
            user,
            client,
            1_000,
            CustomerSaleAllocation(oldTarget.Id, 800));
        await database.AuthenticateAsync(user);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(source.Id, CustomerSaleAllocation(newTarget.Id, 201)));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.AllocationsExceedAvailableAdvance,
            expectedPaymentCount: 1,
            expectedAllocationCount: 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Customer_target_outstanding_is_enforced(bool targetSale)
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser($"customer-outstanding-{targetSale}", UserRole.Responsable);
        var client = database.AddClient("Customer Outstanding");
        var source = database.AddCustomerPayment(user, client, 200);
        CustomerAdvanceAllocationInput target;
        if (targetSale)
        {
            var sale = database.AddSale(user, client, 500);
            var oldPayment = database.AddCustomerPayment(user, client, 450);
            database.AddCustomerAllocation(oldPayment, CustomerSaleAllocation(sale.Id, 450));
            target = CustomerSaleAllocation(sale.Id, 51);
        }
        else
        {
            var receivable = database.AddReceivable(user, client, 500);
            var oldPayment = database.AddCustomerPayment(user, client, 450);
            database.AddCustomerAllocation(oldPayment, CustomerReceivableAllocation(receivable.Id, 450));
            target = CustomerReceivableAllocation(receivable.Id, 51);
        }

        await database.AuthenticateAsync(user);
        var allocationsBefore = database.Context.CustomerPaymentAllocations.Count();

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(source.Id, target));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.AllocationExceedsOutstanding,
            expectedPaymentCount: 2,
            expectedAllocationCount: allocationsBefore);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Customer_target_from_another_client_is_rejected(bool targetSale)
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser($"customer-owner-{targetSale}", UserRole.Responsable);
        var sourceClient = database.AddClient("Source Customer");
        var otherClient = database.AddClient("Other Customer");
        var source = database.AddCustomerPayment(user, sourceClient, 200);
        var target = targetSale
            ? CustomerSaleAllocation(database.AddSale(user, otherClient, 200).Id, 100)
            : CustomerReceivableAllocation(database.AddReceivable(user, otherClient, 200).Id, 100);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(source.Id, target));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.ObligationBelongsToDifferentClient,
            expectedPaymentCount: 1);
    }

    [Fact]
    public async Task Anonymous_sale_is_not_an_eligible_customer_target()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-anonymous-target", UserRole.Responsable);
        var client = database.AddClient("Known Customer");
        var source = database.AddCustomerPayment(user, client, 200);
        var anonymousSale = database.AddSale(user, client: null, 200);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(source.Id, CustomerSaleAllocation(anonymousSale.Id, 100)));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.ObligationBelongsToDifferentClient,
            expectedPaymentCount: 1);
    }

    [Fact]
    public async Task Anonymous_customer_payment_is_not_an_eligible_advance_source()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-anonymous-source", UserRole.Responsable);
        var client = database.AddClient("Known Target Customer");
        var target = database.AddSale(user, client, 200);
        var source = database.AddCustomerPayment(user, client: null, 200);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(source.Id, CustomerSaleAllocation(target.Id, 100)));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.SourcePaymentNotEligible,
            expectedPaymentCount: 1);
    }

    [Theory]
    [InlineData(-1L, ApplyCustomerAdvanceFailureReason.InvalidSourcePayment)]
    [InlineData(0L, ApplyCustomerAdvanceFailureReason.InvalidSourcePayment)]
    [InlineData(999999L, ApplyCustomerAdvanceFailureReason.SourcePaymentNotFound)]
    public async Task Invalid_or_missing_customer_source_is_rejected(
        long paymentId,
        ApplyCustomerAdvanceFailureReason expectedReason)
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser($"customer-source-{paymentId}", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(paymentId, CustomerSaleAllocation(1, 1)));

        AssertCustomerFailure(database, result, expectedReason);
    }

    [Fact]
    public async Task Missing_customer_target_is_rejected()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-missing-target", UserRole.Responsable);
        var client = database.AddClient("Customer Missing Target");
        var source = database.AddCustomerPayment(user, client, 100);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(source.Id, CustomerSaleAllocation(999_999, 100)));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.ObligationNotFound,
            expectedPaymentCount: 1);
    }

    [Theory]
    [MemberData(nameof(MissingCustomerAllocations))]
    public async Task Missing_customer_allocations_are_rejected(
        IReadOnlyCollection<CustomerAdvanceAllocationInput>? allocations)
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-missing-input", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            new ApplyCustomerAdvanceCommand(1, allocations));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.MissingAllocations);
    }

    public static TheoryData<IReadOnlyCollection<CustomerAdvanceAllocationInput>?>
        MissingCustomerAllocations =>
        new()
        {
            null,
            Array.Empty<CustomerAdvanceAllocationInput>()
        };

    [Theory]
    [MemberData(nameof(InvalidCustomerAllocations))]
    public async Task Invalid_customer_allocation_is_rejected(CustomerAdvanceAllocationInput input)
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser($"customer-invalid-{Guid.NewGuid():N}", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(1, input));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.InvalidAllocation);
    }

    public static TheoryData<CustomerAdvanceAllocationInput> InvalidCustomerAllocations =>
        new()
        {
            new CustomerAdvanceAllocationInput(null, null, 100),
            new CustomerAdvanceAllocationInput(1, 1, 100),
            new CustomerAdvanceAllocationInput(1, null, 0),
            new CustomerAdvanceAllocationInput(1, null, -1),
            new CustomerAdvanceAllocationInput(0, null, 100),
            new CustomerAdvanceAllocationInput(null, -1, 100)
        };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Duplicate_customer_target_is_rejected(bool targetSale)
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser($"customer-duplicate-{targetSale}", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        var target = targetSale
            ? CustomerSaleAllocation(1, 50)
            : CustomerReceivableAllocation(1, 50);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(1, target, target));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.DuplicateAllocationTarget);
    }

    [Fact]
    public async Task Unauthenticated_customer_advance_is_rejected()
    {
        using var database = new AdvanceTestDatabase();

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(1, CustomerSaleAllocation(1, 1)));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.NotAuthenticated);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("sales-modify")]
    [InlineData("modify-without-view")]
    public async Task Insufficient_customer_permissions_are_rejected(string permissionCase)
    {
        using var database = new AdvanceTestDatabase();
        var employee = database.AddUser($"customer-permission-{permissionCase}", UserRole.Employe);
        var client = database.AddClient("Customer Permission");
        var target = database.AddSale(employee, client, 100);
        var source = database.AddCustomerPayment(employee, client, 100);
        switch (permissionCase)
        {
            case "create":
                database.Grant(employee, PermissionModule.CustomerPayments, true, true, false);
                break;
            case "sales-modify":
                database.Grant(employee, PermissionModule.Sales, true, false, true);
                break;
            default:
                database.Grant(employee, PermissionModule.CustomerPayments, false, false, true);
                break;
        }

        await database.AuthenticateAsync(employee);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(source.Id, CustomerSaleAllocation(target.Id, 100)));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.Forbidden,
            expectedPaymentCount: 1);
    }

    [Fact]
    public async Task Employee_with_effective_customer_payment_modify_can_apply_advance()
    {
        using var database = new AdvanceTestDatabase();
        var employee = database.AddUser("customer-modify", UserRole.Employe);
        var client = database.AddClient("Customer Modify");
        var target = database.AddSale(employee, client, 100);
        var source = database.AddCustomerPayment(employee, client, 100);
        database.Grant(employee, PermissionModule.CustomerPayments, true, false, true);
        await database.AuthenticateAsync(employee);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(source.Id, CustomerSaleAllocation(target.Id, 100)));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Corrupted_customer_source_allocation_raises_technical_error()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-source-corruption", UserRole.Responsable);
        var client = database.AddClient("Customer Source Corruption");
        var oldTarget = database.AddSale(user, client, 200);
        var newTarget = database.AddSale(user, client, 100);
        var source = database.AddCustomerPayment(
            user,
            client,
            100,
            CustomerSaleAllocation(oldTarget.Id, 150));
        await database.AuthenticateAsync(user);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.CustomerAdvanceService.ApplyAsync(
                CustomerCommand(source.Id, CustomerSaleAllocation(newTarget.Id, 1))));

        Assert.Single(database.Context.CustomerPaymentAllocations);
    }

    [Fact]
    public async Task Corrupted_customer_target_allocation_raises_technical_error()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-target-corruption", UserRole.Responsable);
        var client = database.AddClient("Customer Target Corruption");
        var target = database.AddSale(user, client, 100);
        var corruptingPayment = database.AddCustomerPayment(user, client, 150);
        database.AddCustomerAllocation(
            corruptingPayment,
            CustomerSaleAllocation(target.Id, 150));
        var source = database.AddCustomerPayment(user, client, 100);
        await database.AuthenticateAsync(user);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.CustomerAdvanceService.ApplyAsync(
                CustomerCommand(source.Id, CustomerSaleAllocation(target.Id, 1))));

        Assert.Single(database.Context.CustomerPaymentAllocations);
    }

    [Fact]
    public async Task Customer_cancellation_is_propagated_without_allocations()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-cancel", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.CustomerAdvanceService.ApplyAsync(
                CustomerCommand(1, CustomerSaleAllocation(1, 1)),
                cancellation.Token));

        Assert.Empty(database.Context.CustomerPaymentAllocations);
    }

    [Fact]
    public async Task Customer_advance_sum_validation_does_not_overflow_long()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-overflow", UserRole.Responsable);
        var client = database.AddClient("Customer Overflow");
        var source = database.AddCustomerPayment(user, client, long.MaxValue);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerAdvanceService.ApplyAsync(
            CustomerCommand(
                source.Id,
                CustomerSaleAllocation(1, long.MaxValue - 1),
                CustomerSaleAllocation(2, 2)));

        AssertCustomerFailure(
            database,
            result,
            ApplyCustomerAdvanceFailureReason.AllocationsExceedAvailableAdvance,
            expectedPaymentCount: 1);
    }

    [Fact]
    public async Task Concurrent_customer_advance_applications_cannot_overuse_source_or_target()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-concurrent", UserRole.Responsable);
        var client = database.AddClient("Customer Concurrent");
        var oldTarget = database.AddSale(user, client, 500);
        var target = database.AddSale(user, client, 500);
        var source = database.AddCustomerPayment(
            user,
            client,
            1_000,
            CustomerSaleAllocation(oldTarget.Id, 500));
        await database.AuthenticateAsync(user);

        var attempts = await RunConcurrentCustomerAttempts(
            database,
            source.Id,
            source.Id,
            target.Id);

        Assert.Single(attempts, attempt => attempt.Result?.Success == true);
        Assert.Single(attempts, attempt => attempt.Result?.Success != true);
        Console.WriteLine($"Customer advance A: {Describe(attempts[0].Result, attempts[0].Exception)}");
        Console.WriteLine($"Customer advance B: {Describe(attempts[1].Result, attempts[1].Exception)}");
        using var verificationScope = database.CreateIndependentScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        Assert.Equal(
            1_000,
            await context.CustomerPaymentAllocations
                .Where(item => item.CustomerPaymentId == source.Id)
                .SumAsync(item => item.AmountMinor));
        Assert.Equal(
            500,
            await context.CustomerPaymentAllocations
                .Where(item => item.SaleId == target.Id)
                .SumAsync(item => item.AmountMinor));
        Assert.Single(context.CustomerPayments);
    }

    [Fact]
    public async Task Concurrent_customer_advances_from_different_sources_cannot_overallocate_target()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("customer-concurrent-target", UserRole.Responsable);
        var client = database.AddClient("Customer Concurrent Target");
        var target = database.AddSale(user, client, 500);
        var firstSource = database.AddCustomerPayment(user, client, 500);
        var secondSource = database.AddCustomerPayment(user, client, 500);
        await database.AuthenticateAsync(user);

        var attempts = await RunConcurrentCustomerAttempts(
            database,
            firstSource.Id,
            secondSource.Id,
            target.Id);

        Assert.Single(attempts, attempt => attempt.Result?.Success == true);
        Assert.Single(attempts, attempt => attempt.Result?.Success != true);
        using var verificationScope = database.CreateIndependentScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        Assert.Equal(
            500,
            await context.CustomerPaymentAllocations
                .Where(item => item.SaleId == target.Id)
                .SumAsync(item => item.AmountMinor));
        Assert.Equal(2, await context.CustomerPayments.CountAsync());
    }

    [Fact]
    public async Task Supplier_advance_can_be_partially_then_fully_applied()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("supplier-partial", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier Partial Advance");
        var oldTarget = database.AddPurchase(user, supplier, 1_000);
        var newTarget = database.AddPurchase(user, supplier, 200);
        var source = database.AddSupplierPayment(
            user,
            supplier,
            1_200,
            SupplierPurchaseAllocation(oldTarget.Id, 1_000));
        var sourceSnapshot = SourceSnapshot(source);
        var oldAllocation = database.Context.SupplierPaymentAllocations.Single();
        await database.AuthenticateAsync(user);

        var first = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(source.Id, SupplierPurchaseAllocation(newTarget.Id, 150)));
        var second = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(source.Id, SupplierPurchaseAllocation(newTarget.Id, 50)));
        var third = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(source.Id, SupplierPurchaseAllocation(newTarget.Id, 1)));

        AssertSupplierSuccess(first, source, 200, 150, 50, 1);
        AssertSupplierSuccess(second, source, 50, 50, 0, 1);
        Assert.False(third.Success);
        Assert.Equal(ApplySupplierAdvanceFailureReason.NoAvailableAdvance, third.FailureReason);
        Assert.Equal(1_200, database.Context.SupplierPaymentAllocations
            .Where(item => item.SupplierPaymentId == source.Id)
            .Sum(item => item.AmountMinor));
        Assert.Equal(1_000, database.Context.SupplierPaymentAllocations
            .Single(item => item.Id == oldAllocation.Id).AmountMinor);
        AssertSourceUnchanged(sourceSnapshot, database.Context.SupplierPayments.Single());
        Assert.Single(database.Context.SupplierPayments);
        Assert.Empty(database.Context.TreasuryOpeningBalances);
        Assert.Empty(database.Context.TreasuryTransfers);
        Assert.Empty(database.Context.UserPermissions);
    }

    [Fact]
    public async Task Supplier_advance_can_target_purchase_and_initial_payable()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("supplier-mixed", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier Mixed Targets");
        var purchase = database.AddPurchase(user, supplier, 300);
        var payable = database.AddPayable(user, supplier, 200);
        var untouched = database.AddPurchase(user, supplier, 400);
        var source = database.AddSupplierPayment(user, supplier, 500);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(
                source.Id,
                SupplierPurchaseAllocation(purchase.Id, 300),
                SupplierPayableAllocation(payable.Id, 150)));

        AssertSupplierSuccess(result, source, 500, 450, 50, 2);
        Assert.DoesNotContain(
            database.Context.SupplierPaymentAllocations,
            item => item.PurchaseId == untouched.Id);
    }

    [Fact]
    public async Task Supplier_allocations_exceeding_available_advance_are_rejected()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("supplier-advance-limit", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier Advance Limit");
        var oldTarget = database.AddPurchase(user, supplier, 800);
        var newTarget = database.AddPurchase(user, supplier, 300);
        var source = database.AddSupplierPayment(
            user,
            supplier,
            1_000,
            SupplierPurchaseAllocation(oldTarget.Id, 800));
        await database.AuthenticateAsync(user);

        var result = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(source.Id, SupplierPurchaseAllocation(newTarget.Id, 201)));

        AssertSupplierFailure(
            database,
            result,
            ApplySupplierAdvanceFailureReason.AllocationsExceedAvailableAdvance,
            expectedPaymentCount: 1,
            expectedAllocationCount: 1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Supplier_target_outstanding_is_enforced(bool targetPurchase)
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser($"supplier-outstanding-{targetPurchase}", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier Outstanding");
        var source = database.AddSupplierPayment(user, supplier, 200);
        SupplierAdvanceAllocationInput target;
        if (targetPurchase)
        {
            var purchase = database.AddPurchase(user, supplier, 500);
            var oldPayment = database.AddSupplierPayment(user, supplier, 450);
            database.AddSupplierAllocation(oldPayment, SupplierPurchaseAllocation(purchase.Id, 450));
            target = SupplierPurchaseAllocation(purchase.Id, 51);
        }
        else
        {
            var payable = database.AddPayable(user, supplier, 500);
            var oldPayment = database.AddSupplierPayment(user, supplier, 450);
            database.AddSupplierAllocation(oldPayment, SupplierPayableAllocation(payable.Id, 450));
            target = SupplierPayableAllocation(payable.Id, 51);
        }

        await database.AuthenticateAsync(user);
        var allocationsBefore = database.Context.SupplierPaymentAllocations.Count();

        var result = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(source.Id, target));

        AssertSupplierFailure(
            database,
            result,
            ApplySupplierAdvanceFailureReason.AllocationExceedsOutstanding,
            expectedPaymentCount: 2,
            expectedAllocationCount: allocationsBefore);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Supplier_target_from_another_supplier_is_rejected(bool targetPurchase)
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser($"supplier-owner-{targetPurchase}", UserRole.Responsable);
        var sourceSupplier = database.AddSupplier("Source Supplier");
        var otherSupplier = database.AddSupplier("Other Supplier");
        var source = database.AddSupplierPayment(user, sourceSupplier, 200);
        var target = targetPurchase
            ? SupplierPurchaseAllocation(database.AddPurchase(user, otherSupplier, 200).Id, 100)
            : SupplierPayableAllocation(database.AddPayable(user, otherSupplier, 200).Id, 100);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(source.Id, target));

        AssertSupplierFailure(
            database,
            result,
            ApplySupplierAdvanceFailureReason.ObligationBelongsToDifferentSupplier,
            expectedPaymentCount: 1);
    }

    [Theory]
    [InlineData(-1L, ApplySupplierAdvanceFailureReason.InvalidSourcePayment)]
    [InlineData(0L, ApplySupplierAdvanceFailureReason.InvalidSourcePayment)]
    [InlineData(999999L, ApplySupplierAdvanceFailureReason.SourcePaymentNotFound)]
    public async Task Invalid_or_missing_supplier_source_is_rejected(
        long paymentId,
        ApplySupplierAdvanceFailureReason expectedReason)
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser($"supplier-source-{paymentId}", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(paymentId, SupplierPurchaseAllocation(1, 1)));

        AssertSupplierFailure(database, result, expectedReason);
    }

    [Fact]
    public async Task Missing_supplier_target_is_rejected()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("supplier-missing-target", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier Missing Target");
        var source = database.AddSupplierPayment(user, supplier, 100);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(source.Id, SupplierPurchaseAllocation(999_999, 100)));

        AssertSupplierFailure(
            database,
            result,
            ApplySupplierAdvanceFailureReason.ObligationNotFound,
            expectedPaymentCount: 1);
    }

    [Theory]
    [MemberData(nameof(InvalidSupplierAllocationCollections))]
    public async Task Invalid_supplier_allocation_collection_is_rejected(
        IReadOnlyCollection<SupplierAdvanceAllocationInput>? allocations,
        ApplySupplierAdvanceFailureReason expectedReason)
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser($"supplier-invalid-{Guid.NewGuid():N}", UserRole.Responsable);
        await database.AuthenticateAsync(user);

        var result = await database.SupplierAdvanceService.ApplyAsync(
            new ApplySupplierAdvanceCommand(1, allocations));

        AssertSupplierFailure(database, result, expectedReason);
    }

    public static TheoryData<
        IReadOnlyCollection<SupplierAdvanceAllocationInput>?,
        ApplySupplierAdvanceFailureReason> InvalidSupplierAllocationCollections =>
        new()
        {
            { null, ApplySupplierAdvanceFailureReason.MissingAllocations },
            { Array.Empty<SupplierAdvanceAllocationInput>(), ApplySupplierAdvanceFailureReason.MissingAllocations },
            { new[] { new SupplierAdvanceAllocationInput(null, null, 1) }, ApplySupplierAdvanceFailureReason.InvalidAllocation },
            { new[] { new SupplierAdvanceAllocationInput(1, 1, 1) }, ApplySupplierAdvanceFailureReason.InvalidAllocation },
            { new[] { new SupplierAdvanceAllocationInput(1, null, 0) }, ApplySupplierAdvanceFailureReason.InvalidAllocation },
            { new[] { new SupplierAdvanceAllocationInput(0, null, 1) }, ApplySupplierAdvanceFailureReason.InvalidAllocation }
        };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Duplicate_supplier_target_is_rejected(bool targetPurchase)
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser($"supplier-duplicate-{targetPurchase}", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        var target = targetPurchase
            ? SupplierPurchaseAllocation(1, 50)
            : SupplierPayableAllocation(1, 50);

        var result = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(1, target, target));

        AssertSupplierFailure(
            database,
            result,
            ApplySupplierAdvanceFailureReason.DuplicateAllocationTarget);
    }

    [Fact]
    public async Task Unauthenticated_supplier_advance_is_rejected()
    {
        using var database = new AdvanceTestDatabase();

        var result = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(1, SupplierPurchaseAllocation(1, 1)));

        AssertSupplierFailure(
            database,
            result,
            ApplySupplierAdvanceFailureReason.NotAuthenticated);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("purchases-modify")]
    [InlineData("modify-without-view")]
    public async Task Insufficient_supplier_permissions_are_rejected(string permissionCase)
    {
        using var database = new AdvanceTestDatabase();
        var employee = database.AddUser($"supplier-permission-{permissionCase}", UserRole.Employe);
        var supplier = database.AddSupplier("Supplier Permission");
        var target = database.AddPurchase(employee, supplier, 100);
        var source = database.AddSupplierPayment(employee, supplier, 100);
        switch (permissionCase)
        {
            case "create":
                database.Grant(employee, PermissionModule.SupplierPayments, true, true, false);
                break;
            case "purchases-modify":
                database.Grant(employee, PermissionModule.Purchases, true, false, true);
                break;
            default:
                database.Grant(employee, PermissionModule.SupplierPayments, false, false, true);
                break;
        }

        await database.AuthenticateAsync(employee);

        var result = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(source.Id, SupplierPurchaseAllocation(target.Id, 100)));

        AssertSupplierFailure(
            database,
            result,
            ApplySupplierAdvanceFailureReason.Forbidden,
            expectedPaymentCount: 1);
    }

    [Fact]
    public async Task Employee_with_effective_supplier_payment_modify_can_apply_advance()
    {
        using var database = new AdvanceTestDatabase();
        var employee = database.AddUser("supplier-modify", UserRole.Employe);
        var supplier = database.AddSupplier("Supplier Modify");
        var target = database.AddPurchase(employee, supplier, 100);
        var source = database.AddSupplierPayment(employee, supplier, 100);
        database.Grant(employee, PermissionModule.SupplierPayments, true, false, true);
        await database.AuthenticateAsync(employee);

        var result = await database.SupplierAdvanceService.ApplyAsync(
            SupplierCommand(source.Id, SupplierPurchaseAllocation(target.Id, 100)));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task Corrupted_supplier_source_allocation_raises_technical_error()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("supplier-source-corruption", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier Source Corruption");
        var oldTarget = database.AddPurchase(user, supplier, 200);
        var newTarget = database.AddPurchase(user, supplier, 100);
        var source = database.AddSupplierPayment(
            user,
            supplier,
            100,
            SupplierPurchaseAllocation(oldTarget.Id, 150));
        await database.AuthenticateAsync(user);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.SupplierAdvanceService.ApplyAsync(
                SupplierCommand(source.Id, SupplierPurchaseAllocation(newTarget.Id, 1))));

        Assert.Single(database.Context.SupplierPaymentAllocations);
    }

    [Fact]
    public async Task Corrupted_supplier_target_allocation_raises_technical_error()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("supplier-target-corruption", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier Target Corruption");
        var target = database.AddPurchase(user, supplier, 100);
        var corruptingPayment = database.AddSupplierPayment(user, supplier, 150);
        database.AddSupplierAllocation(
            corruptingPayment,
            SupplierPurchaseAllocation(target.Id, 150));
        var source = database.AddSupplierPayment(user, supplier, 100);
        await database.AuthenticateAsync(user);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.SupplierAdvanceService.ApplyAsync(
                SupplierCommand(source.Id, SupplierPurchaseAllocation(target.Id, 1))));

        Assert.Single(database.Context.SupplierPaymentAllocations);
    }

    [Fact]
    public async Task Supplier_cancellation_is_propagated_without_allocations()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("supplier-cancel", UserRole.Responsable);
        await database.AuthenticateAsync(user);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SupplierAdvanceService.ApplyAsync(
                SupplierCommand(1, SupplierPurchaseAllocation(1, 1)),
                cancellation.Token));

        Assert.Empty(database.Context.SupplierPaymentAllocations);
    }

    [Fact]
    public async Task Concurrent_supplier_advance_applications_cannot_overuse_source_or_target()
    {
        using var database = new AdvanceTestDatabase();
        var user = database.AddUser("supplier-concurrent", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier Concurrent");
        var oldTarget = database.AddPurchase(user, supplier, 500);
        var target = database.AddPurchase(user, supplier, 500);
        var source = database.AddSupplierPayment(
            user,
            supplier,
            1_000,
            SupplierPurchaseAllocation(oldTarget.Id, 500));
        await database.AuthenticateAsync(user);

        var attempts = await RunConcurrentSupplierAttempts(database, source.Id, target.Id);

        Assert.Single(attempts, attempt => attempt.Result?.Success == true);
        Assert.Single(attempts, attempt => attempt.Result?.Success != true);
        Console.WriteLine($"Supplier advance A: {Describe(attempts[0].Result, attempts[0].Exception)}");
        Console.WriteLine($"Supplier advance B: {Describe(attempts[1].Result, attempts[1].Exception)}");
        using var verificationScope = database.CreateIndependentScope();
        var context = verificationScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        Assert.Equal(
            1_000,
            await context.SupplierPaymentAllocations
                .Where(item => item.SupplierPaymentId == source.Id)
                .SumAsync(item => item.AmountMinor));
        Assert.Equal(
            500,
            await context.SupplierPaymentAllocations
                .Where(item => item.PurchaseId == target.Id)
                .SumAsync(item => item.AmountMinor));
        Assert.Single(context.SupplierPayments);
    }

    private static async Task<CustomerAttempt[]> RunConcurrentCustomerAttempts(
        AdvanceTestDatabase database,
        long firstSourcePaymentId,
        long secondSourcePaymentId,
        long targetSaleId)
    {
        using var firstScope = database.CreateIndependentScope();
        using var secondScope = database.CreateIndependentScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        await firstContext.Database.OpenConnectionAsync();
        await secondContext.Database.OpenConnectionAsync();
        Assert.NotSame(firstContext.Database.GetDbConnection(), secondContext.Database.GetDbConnection());
        var firstService = firstScope.ServiceProvider.GetRequiredService<ICustomerAdvanceService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<ICustomerAdvanceService>();
        var firstCommand = CustomerCommand(
            firstSourcePaymentId,
            CustomerSaleAllocation(targetSaleId, 500));
        var secondCommand = CustomerCommand(
            secondSourcePaymentId,
            CustomerSaleAllocation(targetSaleId, 500));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(async () =>
        {
            await gate.Task;
            return await CaptureCustomerAttempt(firstService, firstCommand);
        });
        var secondTask = Task.Run(async () =>
        {
            await gate.Task;
            return await CaptureCustomerAttempt(secondService, secondCommand);
        });
        gate.SetResult();
        var attempts = await Task.WhenAll(firstTask, secondTask);
        firstContext.Database.CloseConnection();
        secondContext.Database.CloseConnection();
        return attempts;
    }

    private static async Task<SupplierAttempt[]> RunConcurrentSupplierAttempts(
        AdvanceTestDatabase database,
        long sourcePaymentId,
        long targetPurchaseId)
    {
        using var firstScope = database.CreateIndependentScope();
        using var secondScope = database.CreateIndependentScope();
        var firstContext = firstScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        await firstContext.Database.OpenConnectionAsync();
        await secondContext.Database.OpenConnectionAsync();
        Assert.NotSame(firstContext.Database.GetDbConnection(), secondContext.Database.GetDbConnection());
        var firstService = firstScope.ServiceProvider.GetRequiredService<ISupplierAdvanceService>();
        var secondService = secondScope.ServiceProvider.GetRequiredService<ISupplierAdvanceService>();
        var command = SupplierCommand(sourcePaymentId, SupplierPurchaseAllocation(targetPurchaseId, 500));
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstTask = Task.Run(async () =>
        {
            await gate.Task;
            return await CaptureSupplierAttempt(firstService, command);
        });
        var secondTask = Task.Run(async () =>
        {
            await gate.Task;
            return await CaptureSupplierAttempt(secondService, command);
        });
        gate.SetResult();
        var attempts = await Task.WhenAll(firstTask, secondTask);
        firstContext.Database.CloseConnection();
        secondContext.Database.CloseConnection();
        return attempts;
    }

    private static async Task<CustomerAttempt> CaptureCustomerAttempt(
        ICustomerAdvanceService service,
        ApplyCustomerAdvanceCommand command)
    {
        try
        {
            return new CustomerAttempt(await service.ApplyAsync(command), null);
        }
        catch (Exception exception)
        {
            return new CustomerAttempt(null, exception);
        }
    }

    private static async Task<SupplierAttempt> CaptureSupplierAttempt(
        ISupplierAdvanceService service,
        ApplySupplierAdvanceCommand command)
    {
        try
        {
            return new SupplierAttempt(await service.ApplyAsync(command), null);
        }
        catch (Exception exception)
        {
            return new SupplierAttempt(null, exception);
        }
    }

    private static string Describe(
        ApplyCustomerAdvanceResult? result,
        Exception? exception) =>
        result?.Success == true
            ? "success"
            : result is not null
                ? $"functional failure ({result.FailureReason})"
                : $"technical failure ({exception!.GetType().Name})";

    private static string Describe(
        ApplySupplierAdvanceResult? result,
        Exception? exception) =>
        result?.Success == true
            ? "success"
            : result is not null
                ? $"functional failure ({result.FailureReason})"
                : $"technical failure ({exception!.GetType().Name})";

    private static ApplyCustomerAdvanceCommand CustomerCommand(
        long paymentId,
        params CustomerAdvanceAllocationInput[] allocations) =>
        new(paymentId, allocations);

    private static CustomerAdvanceAllocationInput CustomerSaleAllocation(long id, long amount) =>
        new(id, null, amount);

    private static CustomerAdvanceAllocationInput CustomerReceivableAllocation(long id, long amount) =>
        new(null, id, amount);

    private static ApplySupplierAdvanceCommand SupplierCommand(
        long paymentId,
        params SupplierAdvanceAllocationInput[] allocations) =>
        new(paymentId, allocations);

    private static SupplierAdvanceAllocationInput SupplierPurchaseAllocation(long id, long amount) =>
        new(id, null, amount);

    private static SupplierAdvanceAllocationInput SupplierPayableAllocation(long id, long amount) =>
        new(null, id, amount);

    private static CustomerPaymentSnapshot SourceSnapshot(CustomerPayment payment) =>
        new(
            payment.Id,
            payment.Reference,
            payment.AmountMinor,
            payment.PaymentMethod,
            payment.PaymentDate,
            payment.Observation,
            payment.ClientId,
            payment.CreatedByUserId,
            payment.CreatedAtUtc);

    private static SupplierPaymentSnapshot SourceSnapshot(SupplierPayment payment) =>
        new(
            payment.Id,
            payment.Reference,
            payment.AmountMinor,
            payment.PaymentMethod,
            payment.PaymentDate,
            payment.Observation,
            payment.SupplierId,
            payment.CreatedByUserId,
            payment.CreatedAtUtc);

    private static void AssertSourceUnchanged(
        CustomerPaymentSnapshot expected,
        CustomerPayment actual) =>
        Assert.Equal(expected, SourceSnapshot(actual));

    private static void AssertSourceUnchanged(
        SupplierPaymentSnapshot expected,
        SupplierPayment actual) =>
        Assert.Equal(expected, SourceSnapshot(actual));

    private static void AssertCustomerSuccess(
        ApplyCustomerAdvanceResult result,
        CustomerPayment source,
        long previous,
        long applied,
        long remaining,
        int count)
    {
        Assert.True(result.Success);
        Assert.Equal(ApplyCustomerAdvanceFailureReason.None, result.FailureReason);
        Assert.Equal(source.Id, result.CustomerPaymentId);
        Assert.Equal(source.Reference, result.CustomerPaymentReference);
        Assert.Equal(previous, result.PreviousAvailableAdvanceMinor);
        Assert.Equal(applied, result.AppliedAmountMinor);
        Assert.Equal(remaining, result.RemainingAvailableAdvanceMinor);
        Assert.Equal(count, result.AllocationCount);
    }

    private static void AssertSupplierSuccess(
        ApplySupplierAdvanceResult result,
        SupplierPayment source,
        long previous,
        long applied,
        long remaining,
        int count)
    {
        Assert.True(result.Success);
        Assert.Equal(ApplySupplierAdvanceFailureReason.None, result.FailureReason);
        Assert.Equal(source.Id, result.SupplierPaymentId);
        Assert.Equal(source.Reference, result.SupplierPaymentReference);
        Assert.Equal(previous, result.PreviousAvailableAdvanceMinor);
        Assert.Equal(applied, result.AppliedAmountMinor);
        Assert.Equal(remaining, result.RemainingAvailableAdvanceMinor);
        Assert.Equal(count, result.AllocationCount);
    }

    private static void AssertCustomerFailure(
        AdvanceTestDatabase database,
        ApplyCustomerAdvanceResult result,
        ApplyCustomerAdvanceFailureReason expected,
        int expectedPaymentCount = 0,
        int expectedAllocationCount = 0)
    {
        Assert.False(result.Success);
        Assert.Equal(expected, result.FailureReason);
        Assert.Null(result.CustomerPaymentId);
        Assert.Null(result.CustomerPaymentReference);
        Assert.Equal(0, result.AppliedAmountMinor);
        Assert.Equal(expectedPaymentCount, database.Context.CustomerPayments.Count());
        Assert.Equal(expectedAllocationCount, database.Context.CustomerPaymentAllocations.Count());
    }

    private static void AssertSupplierFailure(
        AdvanceTestDatabase database,
        ApplySupplierAdvanceResult result,
        ApplySupplierAdvanceFailureReason expected,
        int expectedPaymentCount = 0,
        int expectedAllocationCount = 0)
    {
        Assert.False(result.Success);
        Assert.Equal(expected, result.FailureReason);
        Assert.Null(result.SupplierPaymentId);
        Assert.Null(result.SupplierPaymentReference);
        Assert.Equal(0, result.AppliedAmountMinor);
        Assert.Equal(expectedPaymentCount, database.Context.SupplierPayments.Count());
        Assert.Equal(expectedAllocationCount, database.Context.SupplierPaymentAllocations.Count());
    }

    private sealed class AdvanceTestDatabase : IDisposable
    {
        private const string Password = "Advance service test password 42!";

        private readonly string _databasePath;
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        private int _referenceSequence;

        public AdvanceTestDatabase()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-Advances-{Guid.NewGuid():N}.db");
            var services = new ServiceCollection();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(WorkflowUtc));
            services.AddInfrastructure($"Data Source={_databasePath};Pooling=False");
            _provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            _scope = _provider.CreateScope();
            Context = _scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
            Context.Database.Migrate();
            PasswordHasher = _scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            AuthenticationService = _scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            CustomerAdvanceService = _scope.ServiceProvider.GetRequiredService<ICustomerAdvanceService>();
            SupplierAdvanceService = _scope.ServiceProvider.GetRequiredService<ISupplierAdvanceService>();
        }

        public FinancialManagementDbContext Context { get; }

        public ICustomerAdvanceService CustomerAdvanceService { get; }

        public ISupplierAdvanceService SupplierAdvanceService { get; }

        private IPasswordHasher PasswordHasher { get; }

        private IAuthenticationService AuthenticationService { get; }

        public IServiceScope CreateIndependentScope() => _provider.CreateScope();

        public User AddUser(string username, UserRole role)
        {
            var user = new User
            {
                FullName = $"Advance Test {username}",
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

        public Sale AddSale(User user, Client? client, long amount)
        {
            var sale = new Sale
            {
                Reference = NextReference("VTE"),
                ClientId = client?.Id,
                SaleDate = BusinessDate,
                TotalAmountMinor = amount,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1),
                UpdatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.Sales.Add(sale);
            Context.SaveChanges();
            return sale;
        }

        public InitialCustomerReceivable AddReceivable(User user, Client client, long amount)
        {
            var receivable = new InitialCustomerReceivable
            {
                Reference = NextReference("CRE"),
                ClientId = client.Id,
                ReceivableDate = BusinessDate.AddDays(-30),
                InitialAmountMinor = amount,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.InitialCustomerReceivables.Add(receivable);
            Context.SaveChanges();
            return receivable;
        }

        public Purchase AddPurchase(User user, Supplier supplier, long amount)
        {
            var purchase = new Purchase
            {
                Reference = NextReference("ACH"),
                SupplierId = supplier.Id,
                PurchaseDate = BusinessDate,
                TotalAmountMinor = amount,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1),
                UpdatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.Purchases.Add(purchase);
            Context.SaveChanges();
            return purchase;
        }

        public InitialSupplierPayable AddPayable(User user, Supplier supplier, long amount)
        {
            var payable = new InitialSupplierPayable
            {
                Reference = NextReference("DET"),
                SupplierId = supplier.Id,
                PayableDate = BusinessDate.AddDays(-30),
                InitialAmountMinor = amount,
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.InitialSupplierPayables.Add(payable);
            Context.SaveChanges();
            return payable;
        }

        public CustomerPayment AddCustomerPayment(
            User user,
            Client? client,
            long amount,
            params CustomerAdvanceAllocationInput[] allocations)
        {
            var payment = new CustomerPayment
            {
                Reference = NextReference("ENC"),
                ClientId = client?.Id,
                PaymentDate = BusinessDate.AddDays(-10),
                AmountMinor = amount,
                PaymentMethod = PaymentMethod.BankTransfer,
                Observation = "Source customer payment",
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.CustomerPayments.Add(payment);
            Context.SaveChanges();
            foreach (var allocation in allocations)
            {
                AddCustomerAllocation(payment, allocation);
            }

            return payment;
        }

        public void AddCustomerAllocation(
            CustomerPayment payment,
            CustomerAdvanceAllocationInput allocation)
        {
            Context.CustomerPaymentAllocations.Add(new CustomerPaymentAllocation
            {
                CustomerPaymentId = payment.Id,
                SaleId = allocation.SaleId,
                InitialCustomerReceivableId = allocation.InitialCustomerReceivableId,
                AmountMinor = allocation.AmountMinor,
                CreatedAtUtc = WorkflowUtc.AddDays(-1)
            });
            Context.SaveChanges();
        }

        public SupplierPayment AddSupplierPayment(
            User user,
            Supplier supplier,
            long amount,
            params SupplierAdvanceAllocationInput[] allocations)
        {
            var payment = new SupplierPayment
            {
                Reference = NextReference("DEC"),
                SupplierId = supplier.Id,
                PaymentDate = BusinessDate.AddDays(-10),
                AmountMinor = amount,
                PaymentMethod = PaymentMethod.BankTransfer,
                Observation = "Source supplier payment",
                CreatedByUserId = user.Id,
                CreatedAtUtc = WorkflowUtc.AddDays(-1)
            };
            Context.SupplierPayments.Add(payment);
            Context.SaveChanges();
            foreach (var allocation in allocations)
            {
                AddSupplierAllocation(payment, allocation);
            }

            return payment;
        }

        public void AddSupplierAllocation(
            SupplierPayment payment,
            SupplierAdvanceAllocationInput allocation)
        {
            Context.SupplierPaymentAllocations.Add(new SupplierPaymentAllocation
            {
                SupplierPaymentId = payment.Id,
                PurchaseId = allocation.PurchaseId,
                InitialSupplierPayableId = allocation.InitialSupplierPayableId,
                AmountMinor = allocation.AmountMinor,
                CreatedAtUtc = WorkflowUtc.AddDays(-1)
            });
            Context.SaveChanges();
        }

        public void Grant(
            User user,
            PermissionModule module,
            bool canView,
            bool canCreate,
            bool canModify)
        {
            Context.UserPermissions.Add(new UserPermission
            {
                UserId = user.Id,
                Module = module,
                CanView = canView,
                CanCreate = canCreate,
                CanModify = canModify
            });
            Context.SaveChanges();
        }

        public async Task AuthenticateAsync(User user)
        {
            var result = await AuthenticationService.AuthenticateAsync(user.Username, Password);
            Assert.True(result.Success);
        }

        private string NextReference(string prefix) =>
            $"{prefix}-TEST-{++_referenceSequence:D6}";

        public void Dispose()
        {
            Context.Database.CloseConnection();
            _scope.Dispose();
            _provider.Dispose();

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
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed record CustomerAttempt(
        ApplyCustomerAdvanceResult? Result,
        Exception? Exception);

    private sealed record SupplierAttempt(
        ApplySupplierAdvanceResult? Result,
        Exception? Exception);

    private sealed record CustomerPaymentSnapshot(
        long Id,
        string Reference,
        long AmountMinor,
        PaymentMethod PaymentMethod,
        DateOnly PaymentDate,
        string? Observation,
        long? ClientId,
        long CreatedByUserId,
        DateTimeOffset CreatedAtUtc);

    private sealed record SupplierPaymentSnapshot(
        long Id,
        string Reference,
        long AmountMinor,
        PaymentMethod PaymentMethod,
        DateOnly PaymentDate,
        string? Observation,
        long SupplierId,
        long CreatedByUserId,
        DateTimeOffset CreatedAtUtc);
}
