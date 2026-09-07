using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class TreasuryOpeningInitializationTests
{
    [Fact]
    public async Task Both_initializers_pass_precheck_but_loser_gets_controlled_failure_and_no_pending_insert()
    {
        using var db = new InitializationAdminTestDatabase();
        db.AddUser();
        var gate = new OpeningInsertGate();
        using var first = db.NewProvider(services => services.AddDbContext<FinancialManagementDbContext>(options => options.AddInterceptors(gate)));
        using var second = db.NewProvider(services => services.AddDbContext<FinancialManagementDbContext>(options => options.AddInterceptors(gate)));
        async Task<CreateTreasuryOpeningBalanceResult> Attempt(ServiceProvider provider, long cash)
        {
            using var scope = provider.CreateScope();
            Assert.True((await scope.ServiceProvider.GetRequiredService<IAuthenticationService>()
                .AuthenticateAsync("owner", InitializationAdminTestDatabase.Password)).Success);
            var result = await scope.ServiceProvider.GetRequiredService<ITreasuryService>()
                .CreateOpeningBalanceAsync(new(new(2026, 1, 1), cash, -cash));
            // A rejected insert must not be retried by the next write on this same scope.
            await scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>().SaveChangesAsync();
            return result;
        }

        var results = await Task.WhenAll(Attempt(first, 100), Attempt(second, 200));
        var winner = Assert.Single(results, result => result.Success);
        Assert.Equal(CreateTreasuryOpeningBalanceFailureReason.AlreadyInitialized,
            Assert.Single(results, result => !result.Success).FailureReason);
        Assert.Equal(2, gate.Arrivals);
        var opening = await db.Context.TreasuryOpeningBalances.AsNoTracking().SingleAsync();
        Assert.Equal(winner.CashAmountMinor, opening.CashAmountMinor);
        Assert.Equal(winner.BankAmountMinor, opening.BankAmountMinor);
    }

    private sealed class OpeningInsertGate : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _arrivals;
        public int Arrivals => _arrivals;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<TreasuryOpeningBalance>().Any(entry => entry.State == EntityState.Added))
            {
                if (Interlocked.Increment(ref _arrivals) == 2) _bothArrived.TrySetResult();
                await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken);
            }
            return result;
        }
    }
}
