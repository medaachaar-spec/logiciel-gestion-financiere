using System.Data;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit.Abstractions;

namespace FinancialManagement.Tests;

public sealed class FinancialDashboardSnapshotTests(ITestOutputHelper output)
{
    private static readonly FinancialDashboardQuery Query = new(null, null);

    [Fact]
    public async Task Real_sale_commits_during_dashboard_snapshot_50_times_without_mixing_metrics()
    {
        for (var iteration = 0; iteration < 50; iteration++)
        {
            await using var db = await DashboardSnapshotTestDatabase.CreateAsync();
            Assert.NotSame(db.Context.Database.GetDbConnection(), db.WriterContext.Database.GetDbConnection());
            await AssertWalAsync(db);
            var before = await InterleaveAsync(db, 2, () => db.Dashboard.GetAsync(Query), () => db.CreateSaleAsync());
            AssertMetrics(before, 0, 0, 0, 0, 0, 0, 0, 0);
            AssertCommonSnapshot(db.Trace, 26);
            Assert.Equal(1, db.Trace.Opens);
            Assert.Equal(1, db.Trace.Closes);
            AssertReleased(db);
            AssertMetrics(await db.Dashboard.GetAsync(Query), 10000, 0, 4000, 0, 6000, 0, 4000, 0);
            AssertReleased(db);
        }
        output.WriteLine("50 independent WAL databases; 50 real writers committed before reader resumed; 0 mixed snapshots.");
    }

    [Fact]
    public async Task Autocommit_reference_reproduces_the_original_mixed_sale_response()
    {
        await using var db = await DashboardSnapshotTestDatabase.CreateAsync();
        // Test-only reference for this known-client, cash-only seed. Production has no bypass.
        async Task<long[]> ReadWithoutSnapshot()
        {
            var sales = await db.Context.Sales.SumAsync(x => x.TotalAmountMinor);
            var collected = await db.Context.CustomerPayments.SumAsync(x => x.AmountMinor);
            var currentSales = await db.Context.Sales.SumAsync(x => x.TotalAmountMinor);
            var allocated = await db.Context.CustomerPaymentAllocations.SumAsync(x => x.AmountMinor);
            var treasury = await db.Treasury.GetSummaryAsync();
            return [sales, collected, currentSales - allocated, treasury.CashBalanceMinor];
        }
        var mixed = await InterleaveAsync(db, 2, ReadWithoutSnapshot, () => db.CreateSaleAsync());
        Assert.Equal(new long[] { 0, 4000, 6000, 4000 }, mixed);
        Assert.Null(db.Trace.Reads[0].Transaction);
        output.WriteLine("Autocommit reference reproduced Sales=0, Collected=4000, Receivables=6000, Cash=4000.");
    }

    [Fact]
    public async Task Writer_before_first_select_is_visible_as_a_complete_post_write_snapshot()
    {
        await using var db = await DashboardSnapshotTestDatabase.CreateAsync();
        var result = await InterleaveAsync(db, 1, () => db.Dashboard.GetAsync(Query), () => db.CreateSaleAsync());
        AssertMetrics(result, 10000, 0, 4000, 0, 6000, 0, 4000, 0);
        AssertCommonSnapshot(db.Trace, 26);
        AssertReleased(db);
    }

    [Fact]
    public async Task Purchase_and_payment_remain_in_the_same_payable_snapshot()
    {
        await using var db = await DashboardSnapshotTestDatabase.CreateAsync();
        // Pause after Purchases were materialized, before their allocations are read.
        var before = await InterleaveAsync(db, 11, () => db.Dashboard.GetAsync(Query), db.CreatePurchaseAsync);
        AssertMetrics(before, 0, 0, 0, 0, 0, 0, 0, 0);
        AssertCommonSnapshot(db.Trace, 26);
        AssertMetrics(await db.Dashboard.GetAsync(Query), 0, 10000, 0, 4000, 0, 6000, -4000, 0);
    }

    [Fact]
    public async Task Transfer_commit_between_treasury_transfer_reads_cannot_create_money()
    {
        await using var db = await DashboardSnapshotTestDatabase.CreateAsync();
        Assert.True((await db.Writer<ITreasuryService>().CreateOpeningBalanceAsync(
            new(new DateOnly(2026, 9, 1), 10000, 20000))).Success);
        // Cash->Bank has already been summed. A new Bank->Cash must remain invisible too.
        var before = await InterleaveAsync(db, 26, () => db.Dashboard.GetAsync(Query), async () =>
            Assert.True((await db.Writer<ITreasuryService>().CreateTransferAsync(
                new(TreasuryAccount.Bank, TreasuryAccount.Cash, 3000, null))).Success));
        AssertMetrics(before, 0, 0, 0, 0, 0, 0, 10000, 20000);
        AssertCommonSnapshot(db.Trace, 26);
        AssertMetrics(await db.Dashboard.GetAsync(Query), 0, 0, 0, 0, 0, 0, 13000, 17000);
    }

    [Theory]
    [InlineData("success", "closed")]
    [InlineData("success", "ef")]
    [InlineData("success", "raw")]
    [InlineData("cancel", "closed")]
    [InlineData("cancel", "ef")]
    [InlineData("cancel", "raw")]
    [InlineData("failure", "closed")]
    [InlineData("failure", "ef")]
    [InlineData("failure", "raw")]
    public async Task Dashboard_releases_only_its_connection_reference_and_recovers(string outcome, string owner)
    {
        await using var db = await DashboardSnapshotTestDatabase.CreateAsync();
        var connection = db.Context.Database.GetDbConnection();
        if (owner == "ef") await db.Context.Database.OpenConnectionAsync();
        if (owner == "raw") await connection.OpenAsync();
        var expectedState = owner == "closed" ? ConnectionState.Closed : ConnectionState.Open;
        using var cancellation = new CancellationTokenSource();
        db.Trace.Start((number, command, token) =>
        {
            if (number == 20)
            {
                if (outcome == "cancel")
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                if (outcome == "failure") command.CommandText = "SELECT * FROM __MissingSnapshotAuditTable";
            }
            return Task.CompletedTask;
        });
        if (outcome == "cancel")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Dashboard.GetAsync(Query, cancellation.Token));
        else if (outcome == "failure")
            await Assert.ThrowsAsync<SqliteException>(() => db.Dashboard.GetAsync(Query));
        else
            AssertMetrics(await db.Dashboard.GetAsync(Query), 0, 0, 0, 0, 0, 0, 0, 0);
        Assert.Null(db.Context.Database.CurrentTransaction);
        Assert.Equal(expectedState, connection.State);
        db.Trace.Start();
        Assert.Empty(await db.WriterContext.Sales.ToListAsync());
        Assert.Empty(await db.WriterContext.CustomerPayments.ToListAsync());
        Assert.Empty(await db.WriterContext.CustomerPaymentAllocations.ToListAsync());
        await db.CreateSaleAsync();
        AssertMetrics(await db.Dashboard.GetAsync(Query), 10000, 0, 4000, 0, 6000, 0, 4000, 0);
        Assert.Null(db.Context.Database.CurrentTransaction);
        Assert.Equal(expectedState, connection.State);
        if (owner == "ef") await db.Context.Database.CloseConnectionAsync();
        if (owner == "raw") await connection.CloseAsync();
        AssertReleased(db);
    }

    [Theory]
    [InlineData("success")]
    [InlineData("cancel")]
    [InlineData("failure")]
    public async Task Treasury_never_ends_or_detaches_borrowed_transaction(string outcome)
    {
        await using var db = await DashboardSnapshotTestDatabase.CreateAsync();
        await db.Context.Database.OpenConnectionAsync();
        var connection = (SqliteConnection)db.Context.Database.GetDbConnection();
        await using (var transaction = connection.BeginTransaction(deferred: true))
        await using (var attachment = await db.Context.Database.UseTransactionAsync(transaction))
        {
            // Uncommitted change makes an accidental Treasury commit/rollback observable.
            await db.Context.Database.ExecuteSqlRawAsync("UPDATE Clients SET Name = 'outer uncommitted'");
            using var cancellation = new CancellationTokenSource();
            db.Trace.Start((number, command, token) =>
            {
                if (number == 6)
                {
                    if (outcome == "cancel") { cancellation.Cancel(); token.ThrowIfCancellationRequested(); }
                    if (outcome == "failure") command.CommandText = "SELECT * FROM __MissingSnapshotAuditTable";
                }
                return Task.CompletedTask;
            });
            if (outcome == "cancel")
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Treasury.GetSummaryAsync(cancellation.Token));
            else if (outcome == "failure")
                await Assert.ThrowsAsync<SqliteException>(() => db.Treasury.GetSummaryAsync());
            else
            {
                Assert.True((await db.Treasury.GetSummaryAsync()).Success);
                AssertCommonSnapshot(db.Trace, 13);
            }
            Assert.Same(attachment, db.Context.Database.CurrentTransaction);
            Assert.Same(connection, transaction.Connection);
            Assert.Equal(ConnectionState.Open, connection.State);
            db.Trace.Start();
            Assert.Equal("outer uncommitted", await db.Context.Clients.Select(x => x.Name).SingleAsync());
            Assert.Equal("Snapshot client", await db.WriterContext.Clients.Select(x => x.Name).SingleAsync());
            await transaction.RollbackAsync();
        }
        await db.Context.Database.CloseConnectionAsync();
        AssertReleased(db);
        Assert.Equal("Snapshot client", await db.WriterContext.Clients.Select(x => x.Name).SingleAsync());
        Assert.True((await db.Treasury.GetSummaryAsync()).Success);
    }

    [Fact]
    public async Task Standalone_treasury_still_owns_and_releases_its_transaction()
    {
        await using var db = await DashboardSnapshotTestDatabase.CreateAsync();
        await db.CreateSaleAsync();
        db.Trace.Start();
        var result = await db.Treasury.GetSummaryAsync();
        Assert.True(result.Success);
        Assert.Equal(4000, result.CashBalanceMinor);
        Assert.Equal(0, result.BankBalanceMinor);
        Assert.Equal(4000, result.TotalTreasuryMinor);
        AssertCommonSnapshot(db.Trace, 13);
        AssertReleased(db);
        db.Trace.Start();
        db.Get<IAuthenticationService>().Logout();
        Assert.Equal(TreasurySummaryFailureReason.NotAuthenticated, (await db.Treasury.GetSummaryAsync()).FailureReason);
        Assert.Empty(db.Trace.Reads);
    }

    [Fact]
    public async Task Standalone_treasury_retains_enum_guard_and_recovers_after_failure()
    {
        await using var db = await DashboardSnapshotTestDatabase.CreateAsync();
        await db.CreateSaleAsync();
        await db.WriterContext.Database.ExecuteSqlRawAsync("UPDATE CustomerPayments SET PaymentMethod = 'Invalid'");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => db.Treasury.GetSummaryAsync());
        Assert.Contains("invalid payment method", error.Message);
        AssertReleased(db);
        await db.WriterContext.Database.ExecuteSqlRawAsync("UPDATE CustomerPayments SET PaymentMethod = 'Cash'");
        Assert.Equal(4000, (await db.Treasury.GetSummaryAsync()).CashBalanceMinor);
        AssertReleased(db);
    }

    private static async Task<T> InterleaveAsync<T>(DashboardSnapshotTestDatabase db, int queryNumber,
        Func<Task<T>> read, Func<Task> write)
    {
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        db.Trace.Start(async (number, _, token) =>
        {
            if (number != queryNumber) return;
            reached.SetResult();
            await resume.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
        });
        var reading = read();
        try
        {
            await reached.Task.WaitAsync(TimeSpan.FromSeconds(30));
            Assert.False(reading.IsCompleted);
            await write(); // Success means the real service committed while the reader was paused.
        }
        finally
        {
            resume.TrySetResult();
            // Await disposal even when writer assertions fail, before disposing the test database.
            await reading;
        }
        return await reading;
    }

    private static void AssertCommonSnapshot(SnapshotTrace trace, int count)
    {
        Assert.Equal(count, trace.Reads.Count);
        var first = trace.Reads[0];
        Assert.NotNull(first.Transaction);
        Assert.All(trace.Reads, read =>
        {
            Assert.Same(first.Connection, read.Connection);
            Assert.Same(first.Transaction, read.Transaction);
            Assert.StartsWith("SELECT", read.Sql);
        });
    }

    private static void AssertReleased(DashboardSnapshotTestDatabase db)
    {
        Assert.Null(db.Context.Database.CurrentTransaction);
        Assert.Equal(ConnectionState.Closed, db.Context.Database.GetDbConnection().State);
    }

    private static void AssertMetrics(FinancialDashboardResult result, long sales, long purchases,
        long collected, long paid, long receivables, long payables, long cash, long bank)
    {
        Assert.True(result.Success);
        Assert.Equal(sales, result.TotalSalesMinor);
        Assert.Equal(purchases, result.TotalPurchasesMinor);
        Assert.Equal(collected, result.CustomerCollectionsMinor);
        Assert.Equal(paid, result.SupplierPaymentsMinor);
        Assert.Equal(0, result.ExpensesMinor);
        Assert.Equal(sales - purchases, result.ResultMinor);
        Assert.Equal(receivables, result.CurrentCustomerReceivablesMinor);
        Assert.Equal(payables, result.CurrentSupplierPayablesMinor);
        Assert.Equal(cash, result.CurrentCashBalanceMinor);
        Assert.Equal(bank, result.CurrentBankBalanceMinor);
        Assert.Equal(cash + bank, result.CurrentTotalTreasuryMinor);
    }

    private static async Task AssertWalAsync(DashboardSnapshotTestDatabase db)
    {
        await db.Context.Database.OpenConnectionAsync();
        await using var command = db.Context.Database.GetDbConnection().CreateCommand();
        command.CommandText = "PRAGMA journal_mode";
        Assert.Equal("wal", await command.ExecuteScalarAsync());
        await db.Context.Database.CloseConnectionAsync();
    }
}
