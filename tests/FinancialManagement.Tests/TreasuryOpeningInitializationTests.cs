using FinancialManagement.Infrastructure;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Treasury;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class TreasuryOpeningInitializationTests
{
    [Fact]
    public async Task Concurrent_initializers_are_serialized_and_loser_gets_controlled_failure_without_pending_insert()
    {
        using var db = new InitializationAdminTestDatabase();
        db.AddUser();

        using var first = db.NewProvider();
        using var second = db.NewProvider();

        var bothReady = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var readyCount = 0;

        async Task<CreateTreasuryOpeningBalanceResult> Attempt(
            ServiceProvider provider,
            long cash)
        {
            using var scope = provider.CreateScope();

            Assert.True(
                (await scope.ServiceProvider
                    .GetRequiredService<IAuthenticationService>()
                    .AuthenticateAsync(
                        "owner",
                        InitializationAdminTestDatabase.Password))
                .Success);

            if (Interlocked.Increment(ref readyCount) == 2)
            {
                bothReady.TrySetResult();
            }

            await bothReady.Task.WaitAsync(TimeSpan.FromSeconds(15));

            var result = await scope.ServiceProvider
                .GetRequiredService<ITreasuryService>()
                .CreateOpeningBalanceAsync(
                    new(new(2026, 1, 1), cash, -cash));

            // A controlled rejection must leave no pending entity that a
            // subsequent SaveChanges could accidentally persist.
            await scope.ServiceProvider
                .GetRequiredService<FinancialManagementDbContext>()
                .SaveChangesAsync();

            return result;
        }

        var results = await Task.WhenAll(
            Attempt(first, 100),
            Attempt(second, 200));

        var winner = Assert.Single(results, result => result.Success);

        Assert.Equal(
            CreateTreasuryOpeningBalanceFailureReason.AlreadyInitialized,
            Assert.Single(results, result => !result.Success).FailureReason);

        var opening = await db.Context.TreasuryOpeningBalances
            .AsNoTracking()
            .SingleAsync();

        Assert.Equal(winner.CashAmountMinor, opening.CashAmountMinor);
        Assert.Equal(winner.BankAmountMinor, opening.BankAmountMinor);
    }
}