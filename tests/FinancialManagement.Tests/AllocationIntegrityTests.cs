using System.Data.Common;
using System.Text.Json;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.SupplierAdvances;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class AllocationIntegrityTests
{
    public static IEnumerable<object[]> Writers()
    {
        foreach (var customer in new[] { true, false })
        foreach (var initial in new[] { true, false })
        foreach (var advance in new[] { true, false })
            yield return [customer, initial, advance];
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task Overallocated_contributing_payment_across_targets_fails_without_mutation(
        bool customer, bool initial, bool advance)
    {
        using var db = await Fixture.CreateAsync(customer);
        var target = db.Target(initial, 1000);
        var other = db.Target(!initial, 1000);
        var corrupt = db.Payment(600);
        db.Allocate(corrupt, target, 400);
        db.Allocate(corrupt, other, 400);
        var source = advance ? db.Payment(1000) : 0;
        var before = await db.FingerprintAsync();

        var result = await db.WriteAsync(advance, source, target, 1000, 100);

        Assert.Equal(new Outcome(false, "DataIntegrityViolation"), result);
        Assert.Equal(before, await db.FingerprintAsync());
        Assert.DoesNotContain(db.Context.ChangeTracker.Entries(), e =>
            e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted);
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task Persisted_target_sum_beyond_long_max_fails_without_SQL_overflow(
        bool customer, bool initial, bool advance)
    {
        using var db = await Fixture.CreateAsync(customer);
        var target = db.Target(initial, long.MaxValue);
        db.Allocate(db.Payment(long.MaxValue), target, long.MaxValue);
        db.Allocate(db.Payment(1), target, 1);
        var source = advance ? db.Payment(1) : 0;
        var before = await db.FingerprintAsync();

        var result = await db.WriteAsync(advance, source, target, 1, 1);

        Assert.Equal(new Outcome(false, "DataIntegrityViolation"), result);
        Assert.Equal(before, await db.FingerprintAsync());
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task Negative_row_cannot_hide_behind_a_positive_aggregate(
        bool customer, bool initial, bool advance)
    {
        using var db = await Fixture.CreateAsync(customer);
        var target = db.Target(initial, 1000);
        var payment = db.Payment(1000);
        db.Allocate(payment, target, 400);
        await db.Context.Database.OpenConnectionAsync();
        await db.Context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
        db.Allocate(payment, target, -100);
        await db.Context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF");
        var source = advance ? db.Payment(1000) : 0;
        var before = await db.FingerprintAsync();

        Assert.Equal(new Outcome(false, "DataIntegrityViolation"),
            await db.WriteAsync(advance, source, target, 1000, 100));
        Assert.Equal(before, await db.FingerprintAsync());
    }

    [Theory]
    [MemberData(nameof(Writers))]
    public async Task Exact_long_max_target_and_payment_are_accepted(
        bool customer, bool initial, bool advance)
    {
        using var db = await Fixture.CreateAsync(customer);
        var target = db.Target(initial, long.MaxValue);
        var source = db.Payment(long.MaxValue);
        db.Allocate(source, target, long.MaxValue - 1);

        var result = await db.WriteAsync(advance, source, target, 1, 1);

        Assert.True(result.Success, result.Reason);
        await db.AssertInvariantsAsync();
        Assert.Equal(long.MaxValue, await db.TargetTotalAsync(target));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Advance_source_sum_beyond_long_max_fails_closed(bool customer)
    {
        using var db = await Fixture.CreateAsync(customer);
        var source = db.Payment(long.MaxValue);
        db.Allocate(source, db.Target(false, long.MaxValue), long.MaxValue);
        db.Allocate(source, db.Target(true, 10), 1);
        var target = db.Target(false, 100);
        var before = await db.FingerprintAsync();

        Assert.Equal(new Outcome(false, "DataIntegrityViolation"),
            await db.WriteAsync(true, source, target, 1, 1));
        Assert.Equal(before, await db.FingerprintAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Advance_rejects_corruption_of_a_previously_funded_obligation(bool customer)
    {
        using var db = await Fixture.CreateAsync(customer);
        var oldTarget = db.Target(true, 500);
        var source = db.Payment(1000);
        db.Allocate(source, oldTarget, 400);
        db.Allocate(db.Payment(200), oldTarget, 200);
        var target = db.Target(false, 1000);
        var before = await db.FingerprintAsync();

        Assert.Equal(new Outcome(false, "DataIntegrityViolation"),
            await db.WriteAsync(true, source, target, 1000, 100));
        Assert.Equal(before, await db.FingerprintAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Payment_list_detail_and_lookup_return_controlled_failure_on_sum_overflow(bool customer)
    {
        using var db = await Fixture.CreateAsync(customer);
        var source = db.Payment(long.MaxValue);
        db.Allocate(source, db.Target(false, long.MaxValue), long.MaxValue);
        db.Allocate(source, db.Target(true, 10), 1);
        var before = await db.FingerprintAsync();

        await db.AssertReadIntegrityFailuresAsync(source);

        Assert.Equal(before, await db.FingerprintAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Zero_persisted_row_is_rejected_by_writes_and_payment_reads(bool customer)
    {
        using var db = await Fixture.CreateAsync(customer);
        var target = db.Target(false, 1000);
        var source = db.Payment(1000);
        await db.Context.Database.OpenConnectionAsync();
        await db.Context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = ON");
        db.Allocate(source, target, 0);
        await db.Context.Database.ExecuteSqlRawAsync("PRAGMA ignore_check_constraints = OFF");
        var before = await db.FingerprintAsync();

        await db.AssertReadIntegrityFailuresAsync(source);
        Assert.Equal(new Outcome(false, "DataIntegrityViolation"),
            await db.WriteAsync(false, 0, target, 1000, 100));
        Assert.Equal(new Outcome(false, "DataIntegrityViolation"),
            await db.WriteAsync(true, source, target, 1000, 100));
        Assert.Equal(before, await db.FingerprintAsync());
    }

    public static IEnumerable<object[]> BoundedRequests()
    {
        foreach (var writer in Writers())
        foreach (var amount in new long[] { -1, 0, 599, 600, 601 })
            yield return [.. writer, amount];
    }

    [Theory]
    [MemberData(nameof(BoundedRequests))]
    public async Task Existing_allocations_and_new_requests_respect_both_limits(
        bool customer, bool initial, bool advance, long amount)
    {
        using var db = await Fixture.CreateAsync(customer);
        var target = db.Target(initial, 1000);
        var source = db.Payment(1000);
        db.Allocate(source, target, 400);
        var before = await db.FingerprintAsync();

        var result = await db.WriteAsync(advance, source, target, 600, amount);

        Assert.Equal(amount is > 0 and <= 600, result.Success);
        if (!result.Success)
            Assert.Equal(before, await db.FingerprintAsync());
        await db.AssertInvariantsAsync();
    }

    public static IEnumerable<object[]> Races()
    {
        foreach (var customer in new[] { true, false })
        foreach (var initial in new[] { true, false })
        foreach (var mode in new[] { "create", "advance", "mixed", "source", "target" })
        foreach (var compatible in new[] { true, false })
            yield return [customer, initial, mode, compatible];
    }

    [Theory]
    [MemberData(nameof(Races))]
    public async Task Concurrent_writers_serialize_validation_and_commit_valid_aggregates(
        bool customer, bool initial, string mode, bool compatible)
    {
        using var db = await Fixture.CreateAsync(customer);
        var target = db.Target(initial, 1000);
        var source = db.Payment(1000);
        var gate = new TransactionGate();
        var secondSource = mode == "target" ? db.Payment(1000) : source;
        var secondTarget = mode == "source" ? db.Target(initial, 1000) : target;
        using var firstProvider = db.NewProvider(gate);
        using var secondProvider = db.NewProvider(gate, new SecondValidationAttemptInterceptor(gate));
        using var firstScope = firstProvider.CreateScope();
        using var secondScope = secondProvider.CreateScope();
        await db.PrepareAsync(firstScope.ServiceProvider);
        await db.PrepareAsync(secondScope.ServiceProvider);
        var firstContext = firstScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        var secondContext = secondScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        Assert.NotSame(firstContext.Database.GetDbConnection(), secondContext.Database.GetDbConnection());
        gate.Armed = true;
        var amount = compatible ? 400 : 600;
        var first = StartWriter(() => db.WriteAsync(mode != "create", source, target, 1000, amount,
            firstScope.ServiceProvider));
        Task<Outcome>? second = null;
        try
        {
            await gate.FirstStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            second = StartWriter(() => db.WriteAsync(mode is "advance" or "source" or "target",
                secondSource, secondTarget, 1000, amount,
                secondScope.ServiceProvider));
            await gate.SecondValidationAttempted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            gate.Release.TrySetResult();
            // A timed-out gate must not leave a writer using a disposed fixture.
            await Task.WhenAll(second is null ? [first] : [first, second])
                .WaitAsync(TimeSpan.FromSeconds(35));
        }
        var outcomes = await Task.WhenAll(first, second!).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(compatible ? 2 : 1, outcomes.Count(o => o.Success));
        Assert.All(outcomes.Where(o => !o.Success), o => Assert.Contains(o.Reason,
            new[] { "AllocationExceedsOutstanding", "AllocationsExceedAvailableAdvance" }));
        var total = await db.TargetTotalAsync(target);
        if (mode == "source")
            total += await db.TargetTotalAsync(secondTarget);
        Assert.Equal(compatible ? 800 : 600, total);
        await db.AssertInvariantsAsync();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EF_transaction_reserves_writer_before_any_read_and_busy_stays_technical(bool customer)
    {
        using var db = await Fixture.CreateAsync(customer);
        var target = db.Target(false, 1000);
        using var provider = db.NewProvider();
        using var scope = provider.CreateScope();
        await db.PrepareAsync(scope.ServiceProvider);
        var contender = scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        ((SqliteConnection)contender.Database.GetDbConnection()).DefaultTimeout = 1;
        var before = await db.FingerprintAsync();
        // There are deliberately no commands between BeginTransactionAsync and
        // the competing write. A deferred transaction would not reserve a writer.
        await using (var transaction = await db.Context.Database.BeginTransactionAsync())
        {
            var error = await Assert.ThrowsAsync<SqliteException>(() =>
                db.WriteAsync(false, 0, target, 1000, 100, scope.ServiceProvider));
            Assert.Contains(error.SqliteErrorCode, new[] { 5, 6 });
        }
        Assert.Equal(before, await db.FingerprintAsync());
        Assert.True((await db.WriteAsync(false, 0, target, 1000, 100, scope.ServiceProvider)).Success);
    }

    private sealed record Outcome(bool Success, string Reason);
    private sealed record Target(long Id, bool Initial);

    // SQLite's async facade can synchronously wait for a native writer lock.
    // Give each contender a dedicated worker so unrelated parallel test cases
    // cannot starve the second writer before it reaches the transaction gate.
    private static Task<Outcome> StartWriter(Func<Task<Outcome>> operation) =>
        Task.Factory.StartNew(operation, CancellationToken.None,
            TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private sealed class TransactionGate : DbTransactionInterceptor
    {
        public bool Armed { get; set; }
        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondValidationAttempted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<DbTransaction> TransactionStartedAsync(
            DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
            CancellationToken cancellationToken = default)
        {
            if (Armed && FirstStarted.TrySetResult())
                await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            return result;
        }
    }

    private sealed class SecondValidationAttemptInterceptor(TransactionGate gate) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            // SEC-001 validates the second session before it can attempt to
            // acquire the writer lock. Observe that invocation here, then let
            // the first writer finish so both outcomes remain testable.
            if (gate.Armed && SessionValidationCommandTestHelper.IsPersistedSessionValidation(command))
                gate.SecondValidationAttempted.TrySetResult();
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class Fixture(bool customer) : IDisposable
    {
        private readonly InitializationAdminTestDatabase db = new();
        private long userId;
        private long partyId;
        public FinancialManagementDbContext Context => db.Context;

        public static async Task<Fixture> CreateAsync(bool customer)
        {
            var fixture = new Fixture(customer);
            fixture.userId = (await fixture.db.LoginResponsibleAsync()).Id;
            if (customer)
            {
                var party = new Client { Name = "Allocation client" };
                fixture.Context.Add(party);
                fixture.Context.SaveChanges();
                fixture.partyId = party.Id;
            }
            else
            {
                var party = new Supplier { Name = "Allocation supplier" };
                fixture.Context.Add(party);
                fixture.Context.SaveChanges();
                fixture.partyId = party.Id;
            }
            return fixture;
        }

        public Target Target(bool initial, long amount)
        {
            var reference = Guid.NewGuid().ToString();
            if (customer && !initial)
            {
                var row = new Sale { ClientId = partyId, Reference = reference,
                    TotalAmountMinor = amount, CreatedByUserId = userId };
                Context.Add(row); Context.SaveChanges(); return new(row.Id, initial);
            }
            if (customer)
            {
                var row = new InitialCustomerReceivable { ClientId = partyId, Reference = reference,
                    InitialAmountMinor = amount, CreatedByUserId = userId };
                Context.Add(row); Context.SaveChanges(); return new(row.Id, initial);
            }
            if (!initial)
            {
                var row = new Purchase { SupplierId = partyId, Reference = reference,
                    TotalAmountMinor = amount, CreatedByUserId = userId };
                Context.Add(row); Context.SaveChanges(); return new(row.Id, initial);
            }
            else
            {
                var row = new InitialSupplierPayable { SupplierId = partyId, Reference = reference,
                    InitialAmountMinor = amount, CreatedByUserId = userId };
                Context.Add(row); Context.SaveChanges(); return new(row.Id, initial);
            }
        }

        public long Payment(long amount)
        {
            if (customer)
            {
                var row = new CustomerPayment { ClientId = partyId, Reference = Guid.NewGuid().ToString(),
                    AmountMinor = amount, PaymentMethod = PaymentMethod.Cash, CreatedByUserId = userId };
                Context.Add(row); Context.SaveChanges(); return row.Id;
            }
            else
            {
                var row = new SupplierPayment { SupplierId = partyId, Reference = Guid.NewGuid().ToString(),
                    AmountMinor = amount, PaymentMethod = PaymentMethod.Cash, CreatedByUserId = userId };
                Context.Add(row); Context.SaveChanges(); return row.Id;
            }
        }

        public void Allocate(long payment, Target target, long amount)
        {
            if (customer)
                Context.Add(new CustomerPaymentAllocation { CustomerPaymentId = payment, AmountMinor = amount,
                    SaleId = target.Initial ? null : target.Id,
                    InitialCustomerReceivableId = target.Initial ? target.Id : null });
            else
                Context.Add(new SupplierPaymentAllocation { SupplierPaymentId = payment, AmountMinor = amount,
                    PurchaseId = target.Initial ? null : target.Id,
                    InitialSupplierPayableId = target.Initial ? target.Id : null });
            Context.SaveChanges();
        }

        public async Task<Outcome> WriteAsync(bool advance, long source, Target target,
            long paymentAmount, long amount, IServiceProvider? provider = null)
        {
            T Get<T>() where T : notnull => provider is null ? db.Get<T>() : provider.GetRequiredService<T>();
            long? documentId = target.Initial ? null : target.Id;
            long? initialId = target.Initial ? target.Id : null;
            if (customer && advance)
            {
                var result = await Get<ICustomerAdvanceService>().ApplyAsync(new(source,
                    [new(documentId, initialId, amount)]));
                return new(result.Success, result.FailureReason.ToString());
            }
            if (customer)
            {
                var result = await Get<ICustomerPaymentService>().CreateAsync(new(partyId, paymentAmount,
                    PaymentMethod.Cash, null, [new(documentId, initialId, amount)]));
                return new(result.Success, result.FailureReason.ToString());
            }
            if (advance)
            {
                var result = await Get<ISupplierAdvanceService>().ApplyAsync(new(source,
                    [new(documentId, initialId, amount)]));
                return new(result.Success, result.FailureReason.ToString());
            }
            else
            {
                var result = await Get<ISupplierPaymentService>().CreateAsync(new(partyId, paymentAmount,
                    PaymentMethod.Cash, null, [new(documentId, initialId, amount)]));
                return new(result.Success, result.FailureReason.ToString());
            }
        }

        public ServiceProvider NewProvider(
            DbTransactionInterceptor? interceptor = null,
            DbCommandInterceptor? readInterceptor = null) =>
            db.NewProvider(services =>
            {
                if (interceptor is not null)
                    services.AddDbContext<FinancialManagementDbContext>(options => options.AddInterceptors(interceptor));
                if (readInterceptor is not null)
                    services.AddDbContext<FinancialManagementDbContext>(options => options.AddInterceptors(readInterceptor));
            });

        public async Task PrepareAsync(IServiceProvider provider)
        {
            Assert.True((await provider.GetRequiredService<IAuthenticationService>()
                .AuthenticateAsync("owner", InitializationAdminTestDatabase.Password)).Success);
            await provider.GetRequiredService<FinancialManagementDbContext>().Database.OpenConnectionAsync();
        }

        public async Task AssertReadIntegrityFailuresAsync(long paymentId)
        {
            if (customer)
            {
                var service = db.Get<ICustomerPaymentService>();
                Assert.Equal(CustomerPaymentListFailureReason.DataIntegrityViolation,
                    (await service.GetPageAsync(new())).FailureReason);
                Assert.Equal(CustomerPaymentDetailFailureReason.DataIntegrityViolation,
                    (await service.GetByIdAsync(paymentId)).FailureReason);
                Assert.Equal(CustomerPaymentObligationLookupFailureReason.DataIntegrityViolation,
                    (await db.Get<ICustomerPaymentObligationLookupService>().SearchAsync(new(
                        partyId, CustomerPaymentObligationLookupPurpose.CreatePayment))).FailureReason);
            }
            else
            {
                var service = db.Get<ISupplierPaymentService>();
                Assert.Equal(SupplierPaymentListFailureReason.DataIntegrityViolation,
                    (await service.GetPageAsync(new())).FailureReason);
                Assert.Equal(SupplierPaymentDetailFailureReason.DataIntegrityViolation,
                    (await service.GetByIdAsync(paymentId)).FailureReason);
                Assert.Equal(SupplierPaymentObligationLookupFailureReason.DataIntegrityViolation,
                    (await db.Get<ISupplierPaymentObligationLookupService>().SearchAsync(new(
                        partyId, SupplierPaymentObligationLookupPurpose.CreatePayment))).FailureReason);
            }
        }

        public async Task<string> FingerprintAsync() => customer
            ? JsonSerializer.Serialize(new { Payments = await Context.CustomerPayments.AsNoTracking().OrderBy(p => p.Id).ToListAsync(),
                Allocations = await Context.CustomerPaymentAllocations.AsNoTracking().OrderBy(a => a.Id).ToListAsync() })
            : JsonSerializer.Serialize(new { Payments = await Context.SupplierPayments.AsNoTracking().OrderBy(p => p.Id).ToListAsync(),
                Allocations = await Context.SupplierPaymentAllocations.AsNoTracking().OrderBy(a => a.Id).ToListAsync() });

        public Task<long> TargetTotalAsync(Target target) => customer
            ? Context.CustomerPaymentAllocations.Where(a => target.Initial
                ? a.InitialCustomerReceivableId == target.Id : a.SaleId == target.Id).SumAsync(a => a.AmountMinor)
            : Context.SupplierPaymentAllocations.Where(a => target.Initial
                ? a.InitialSupplierPayableId == target.Id : a.PurchaseId == target.Id).SumAsync(a => a.AmountMinor);

        public async Task AssertInvariantsAsync()
        {
            if (customer)
            {
                var allocations = await Context.CustomerPaymentAllocations.AsNoTracking().ToListAsync();
                Assert.All(allocations, a => Assert.True(a.AmountMinor > 0));
                foreach (var payment in await Context.CustomerPayments.AsNoTracking().ToListAsync())
                    Assert.InRange(allocations.Where(a => a.CustomerPaymentId == payment.Id).Sum(a => a.AmountMinor), 0, payment.AmountMinor);
                foreach (var target in await Context.Sales.AsNoTracking().ToListAsync())
                    Assert.InRange(await TargetTotalAsync(new(target.Id, false)), 0, target.TotalAmountMinor);
                foreach (var target in await Context.InitialCustomerReceivables.AsNoTracking().ToListAsync())
                    Assert.InRange(await TargetTotalAsync(new(target.Id, true)), 0, target.InitialAmountMinor);
            }
            else
            {
                var allocations = await Context.SupplierPaymentAllocations.AsNoTracking().ToListAsync();
                Assert.All(allocations, a => Assert.True(a.AmountMinor > 0));
                foreach (var payment in await Context.SupplierPayments.AsNoTracking().ToListAsync())
                    Assert.InRange(allocations.Where(a => a.SupplierPaymentId == payment.Id).Sum(a => a.AmountMinor), 0, payment.AmountMinor);
                foreach (var target in await Context.Purchases.AsNoTracking().ToListAsync())
                    Assert.InRange(await TargetTotalAsync(new(target.Id, false)), 0, target.TotalAmountMinor);
                foreach (var target in await Context.InitialSupplierPayables.AsNoTracking().ToListAsync())
                    Assert.InRange(await TargetTotalAsync(new(target.Id, true)), 0, target.InitialAmountMinor);
            }
        }

        public void Dispose() => db.Dispose();
    }
}
