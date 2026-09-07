using FinancialManagement.Application.InitialCustomerReceivables;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Tests;

public sealed class InitialCustomerReceivableReadServiceTests
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
        var party = new Client { Name = "Historical Client" };
        db.Context.Clients.Add(party);
        await db.Context.SaveChangesAsync();
        var service = db.Get<IInitialCustomerReceivableService>();
        var created = await service.CreateAsync(new(party.Id, 1000, Day, Day.AddDays(30), "Historical obligation"));
        Assert.True(created.Success);
        if (allocated > 0)
        {
            var payment = await db.Get<ICustomerPaymentService>().CreateAsync(new(
                party.Id, allocated, PaymentMethod.Cash, null,
                [new CustomerPaymentAllocationInput(null, created.InitialCustomerReceivableId, allocated)]));
            Assert.True(payment.Success);
        }
        var result = await service.GetPageAsync(new());
        Assert.True(result.Success);
        Assert.Equal(1, result.TotalCount);
        Assert.Equal(1, result.TotalPages);
        var item = Assert.Single(result.Items);
        Assert.Equal(created.InitialCustomerReceivableId, item.Id);
        Assert.Equal(created.Reference, item.Reference);
        Assert.Equal(party.Id, item.ClientId);
        Assert.Equal(party.Name, item.ClientName);
        Assert.Equal(Day, item.ReceivableDate);
        Assert.Equal(Day.AddDays(30), item.DueDate);
        Assert.Equal("Historical obligation", item.Observation);
        Assert.Equal(1000, item.InitialAmountMinor);
        Assert.Equal(1000 - allocated, item.OutstandingAmountMinor);
        Assert.Equal(1000, (await db.Context.InitialCustomerReceivables.AsNoTracking().SingleAsync()).InitialAmountMinor);
        Assert.Empty(await db.Context.Sales.ToListAsync());
    }

    [Fact]
    public async Task Pagination_filter_null_fields_and_empty_page_are_supported()
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        var first = new Client { Name = "First" };
        var second = new Client { Name = "Second" };
        db.Context.Clients.AddRange(first, second);
        await db.Context.SaveChangesAsync();
        var service = db.Get<IInitialCustomerReceivableService>();
        var older = await service.CreateAsync(new(first.Id, 100, Day, null, null));
        var newer = await service.CreateAsync(new(first.Id, 200, Day.AddDays(1), null, null));
        Assert.True((await service.CreateAsync(new(second.Id, 300, Day, null, null))).Success);
        var page = await service.GetPageAsync(new(1, 1, first.Id));
        Assert.True(page.Success);
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.TotalPages);
        Assert.Equal(newer.InitialCustomerReceivableId, Assert.Single(page.Items).Id);
        var next = await service.GetPageAsync(new(2, 1, first.Id));
        var item = Assert.Single(next.Items);
        Assert.Equal(older.InitialCustomerReceivableId, item.Id);
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
        var result = await db.Get<IInitialCustomerReceivableService>().GetPageAsync(new());
        Assert.Equal(authenticated ? InitialCustomerReceivableListFailureReason.Forbidden
            : InitialCustomerReceivableListFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData(0, 50, null, InitialCustomerReceivableListFailureReason.InvalidPageNumber)]
    [InlineData(int.MaxValue, 200, null, InitialCustomerReceivableListFailureReason.InvalidPageNumber)]
    [InlineData(1, 0, null, InitialCustomerReceivableListFailureReason.InvalidPageSize)]
    [InlineData(1, 201, null, InitialCustomerReceivableListFailureReason.InvalidPageSize)]
    [InlineData(1, 50, 0L, InitialCustomerReceivableListFailureReason.InvalidClientId)]
    public async Task Invalid_queries_return_controlled_failure(int page, int size, long? partyId, InitialCustomerReceivableListFailureReason failure)
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        var result = await db.Get<IInitialCustomerReceivableService>().GetPageAsync(new(page, size, partyId));
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
            db.Get<IInitialCustomerReceivableService>().GetPageAsync(new(), cancellation.Token));
        Assert.Empty(await db.Context.InitialCustomerReceivables.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Corrupt_overallocated_or_overflowed_total_is_rejected_without_clamping(bool overflow)
    {
        using var db = new InitializationAdminTestDatabase();
        var owner = await db.LoginResponsibleAsync();
        var party = new Client { Name = "Party" };
        db.Context.Clients.Add(party);
        await db.Context.SaveChangesAsync();
        var service = db.Get<IInitialCustomerReceivableService>();
        var created = await service.CreateAsync(new(party.Id, 100, Day, null, null));
        // Bypass the normal write service deliberately to exercise the local read integrity guard.
        var payment = new CustomerPayment
        {
            Reference = "CorruptAggregate", ClientId = party.Id, AmountMinor = long.MaxValue,
            PaymentDate = Day, PaymentMethod = PaymentMethod.Cash, CreatedByUserId = owner.Id
        };
        db.Context.CustomerPayments.Add(payment);
        await db.Context.SaveChangesAsync();
        db.Context.CustomerPaymentAllocations.Add(new()
        {
            CustomerPaymentId = payment.Id, InitialCustomerReceivableId = created.InitialCustomerReceivableId, AmountMinor = overflow ? long.MaxValue : 101
        });
        if (overflow)
            db.Context.CustomerPaymentAllocations.Add(new()
            { CustomerPaymentId = payment.Id, InitialCustomerReceivableId = created.InitialCustomerReceivableId, AmountMinor = 1 });
        await db.Context.SaveChangesAsync();
        var result = await service.GetPageAsync(new());
        Assert.Equal(InitialCustomerReceivableListFailureReason.DataIntegrityViolation, result.FailureReason);
        Assert.Empty(result.Items);
        Assert.Equal(overflow ? 2 : 1, await db.Context.CustomerPaymentAllocations.CountAsync());
    }
}
