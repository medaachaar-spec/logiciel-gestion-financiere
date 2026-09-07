using FinancialManagement.Application.InitialSupplierPayables;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Tests;

public sealed class InitialSupplierPayableReadServiceTests
{
    private static readonly DateOnly Day = new(2024, 1, 15);

    [Theory]
    [InlineData(0L)]
    [InlineData(400L)]
    [InlineData(1000L)]
    public async Task List_includes_original_and_derived_outstanding_after_real_payments(long allocated)
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        var party = new Supplier { Name = "Historical Supplier" };
        db.Context.Suppliers.Add(party);
        await db.Context.SaveChangesAsync();
        var service = db.Get<IInitialSupplierPayableService>();
        var created = await service.CreateAsync(new(party.Id, 1000, Day, Day.AddDays(30), "Historical obligation"));
        Assert.True(created.Success);
        if (allocated > 0)
        {
            var payment = await db.Get<ISupplierPaymentService>().CreateAsync(new(
                party.Id, allocated, PaymentMethod.Cash, null,
                [new SupplierPaymentAllocationInput(null, created.InitialSupplierPayableId, allocated)]));
            Assert.True(payment.Success);
        }
        var result = await service.GetPageAsync(new());
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        var item = Assert.Single(result.Items);
        Assert.Equal(created.InitialSupplierPayableId, item.Id);
        Assert.Equal(created.Reference, item.Reference);
        Assert.Equal(party.Id, item.SupplierId);
        Assert.Equal(party.Name, item.SupplierName);
        Assert.Equal(Day, item.PayableDate);
        Assert.Equal(Day.AddDays(30), item.DueDate);
        Assert.Equal("Historical obligation", item.Observation);
        Assert.Equal(1000, item.InitialAmountMinor);
        Assert.Equal(1000 - allocated, item.OutstandingAmountMinor);
        Assert.Equal(1000, (await db.Context.InitialSupplierPayables.AsNoTracking().SingleAsync()).InitialAmountMinor);
        Assert.Empty(await db.Context.Purchases.ToListAsync());
    }

    [Fact]
    public async Task Pagination_filter_null_fields_and_empty_page_are_supported()
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        var first = new Supplier { Name = "First" };
        var second = new Supplier { Name = "Second" };
        db.Context.Suppliers.AddRange(first, second);
        await db.Context.SaveChangesAsync();
        var service = db.Get<IInitialSupplierPayableService>();
        var older = await service.CreateAsync(new(first.Id, 100, Day, null, null));
        var newer = await service.CreateAsync(new(first.Id, 200, Day.AddDays(1), null, null));
        Assert.True((await service.CreateAsync(new(second.Id, 300, Day, null, null))).Success);
        var page = await service.GetPageAsync(new(1, 1, first.Id));
        Assert.True(page.Success);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(newer.InitialSupplierPayableId, Assert.Single(page.Items).Id);
        var next = await service.GetPageAsync(new(2, 1, first.Id));
        var item = Assert.Single(next.Items);
        Assert.Equal(older.InitialSupplierPayableId, item.Id);
        Assert.Null(item.DueDate);
        Assert.Null(item.Observation);
        var empty = await service.GetPageAsync(new(3, 1, first.Id));
        Assert.True(empty.Success);
        Assert.Empty(empty.Items);
        Assert.Equal(2, empty.TotalCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Read_is_responsible_only(bool authenticated)
    {
        using var db = new InitializationAdminTestDatabase();
        if (authenticated)
        {
            var employee = db.AddUser("employee", UserRole.Employe);
            foreach (var module in Enum.GetValues<PermissionModule>())
                db.Context.UserPermissions.Add(new UserPermission
                { UserId = employee.Id, Module = module, CanView = true, CanCreate = true, CanModify = true });
            await db.Context.SaveChangesAsync();
            await db.LoginAsync("employee");
        }
        var result = await db.Get<IInitialSupplierPayableService>().GetPageAsync(new());
        Assert.Equal(authenticated ? InitialSupplierPayableListFailureReason.Forbidden
            : InitialSupplierPayableListFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData(0, 50, null, InitialSupplierPayableListFailureReason.InvalidPageNumber)]
    [InlineData(int.MaxValue, 200, null, InitialSupplierPayableListFailureReason.InvalidPageNumber)]
    [InlineData(1, 0, null, InitialSupplierPayableListFailureReason.InvalidPageSize)]
    [InlineData(1, 201, null, InitialSupplierPayableListFailureReason.InvalidPageSize)]
    [InlineData(1, 50, 0L, InitialSupplierPayableListFailureReason.InvalidSupplierId)]
    public async Task Invalid_queries_return_controlled_failure(int page, int size, long? partyId, InitialSupplierPayableListFailureReason failure)
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        var result = await db.Get<IInitialSupplierPayableService>().GetPageAsync(new(page, size, partyId));
        Assert.Equal(failure, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Cancellation_is_propagated_without_writes()
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            db.Get<IInitialSupplierPayableService>().GetPageAsync(new(), cancellation.Token));
        Assert.Empty(await db.Context.InitialSupplierPayables.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrupt_overallocated_or_overflowed_total_is_rejected_without_clamping(bool overflow)
    {
        using var db = new InitializationAdminTestDatabase();
        var owner = await db.LoginResponsibleAsync();
        var party = new Supplier { Name = "Party" };
        db.Context.Suppliers.Add(party);
        await db.Context.SaveChangesAsync();
        var service = db.Get<IInitialSupplierPayableService>();
        var created = await service.CreateAsync(new(party.Id, 100, Day, null, null));
        // Bypass the normal write service deliberately to exercise the local read integrity guard.
        var payment = new SupplierPayment
        {
            Reference = "CorruptAggregate", SupplierId = party.Id, AmountMinor = long.MaxValue,
            PaymentDate = Day, PaymentMethod = PaymentMethod.Cash, CreatedByUserId = owner.Id
        };
        db.Context.SupplierPayments.Add(payment);
        await db.Context.SaveChangesAsync();
        db.Context.SupplierPaymentAllocations.Add(new()
        {
            SupplierPaymentId = payment.Id, InitialSupplierPayableId = created.InitialSupplierPayableId, AmountMinor = overflow ? long.MaxValue : 101
        });
        if (overflow)
            db.Context.SupplierPaymentAllocations.Add(new()
            { SupplierPaymentId = payment.Id, InitialSupplierPayableId = created.InitialSupplierPayableId, AmountMinor = 1 });
        await db.Context.SaveChangesAsync();
        var result = await service.GetPageAsync(new());
        Assert.Equal(InitialSupplierPayableListFailureReason.DataIntegrityViolation, result.FailureReason);
        Assert.Empty(result.Items);
        Assert.Equal(overflow ? 2 : 1, await db.Context.SupplierPaymentAllocations.CountAsync());
    }
}
