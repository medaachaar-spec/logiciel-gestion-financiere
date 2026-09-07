using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Initialization;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class ApplicationInitializationServiceTests
{
    [Fact]
    public async Task Empty_database_requires_bootstrap_and_cannot_authenticate()
    {
        using var db = new InitializationAdminTestDatabase();
        var state = await db.Initialization.GetStateAsync();
        Assert.True(state.RequiresBootstrap);
        Assert.False(state.HasResponsibleUser);
        Assert.False(state.HasOpeningBalances);
        Assert.False(state.IsInitializationComplete);
        Assert.False((await db.Get<IAuthenticationService>().AuthenticateAsync("owner", InitializationAdminTestDatabase.Password)).Success);
    }

    [Fact]
    public async Task Bootstrap_creates_one_active_responsible_with_canonical_hash_without_auto_login()
    {
        using var db = new InitializationAdminTestDatabase();
        var result = await db.Initialization.BootstrapResponsibleAsync(Command());
        Assert.True(result.Success);
        var user = await db.Context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(result.UserId, user.Id);
        Assert.Equal(UserRole.Responsable, user.Role);
        Assert.True(user.IsActive);
        Assert.Equal("First Responsible", user.FullName);
        Assert.Equal("owner", user.Username);
        Assert.Equal(InitializationAdminTestDatabase.Now, user.CreatedAtUtc);
        Assert.Equal(user.CreatedAtUtc, user.UpdatedAtUtc);
        Assert.NotEqual(InitializationAdminTestDatabase.Password, user.PasswordHash);
        Assert.True(db.Get<IPasswordHasher>().VerifyPassword(user.PasswordHash, InitializationAdminTestDatabase.Password));
        Assert.False(db.Get<ICurrentUserSession>().IsAuthenticated);
        Assert.Empty(await db.Context.UserPermissions.ToListAsync());
        Assert.Empty(await db.Context.ApplicationSettings.ToListAsync());
        Assert.Empty(await db.Context.TreasuryOpeningBalances.ToListAsync());
        await db.LoginAsync("owner");
    }

    [Theory]
    [InlineData(UserRole.Responsable, true)]
    [InlineData(UserRole.Responsable, false)]
    [InlineData(UserRole.Employe, true)]
    [InlineData(UserRole.Employe, false)]
    public async Task Any_existing_user_blocks_bootstrap_even_inactive_or_employee(UserRole role, bool active)
    {
        using var db = new InitializationAdminTestDatabase();
        var existing = db.AddUser("existing", role, active);
        var result = await db.Initialization.BootstrapResponsibleAsync(Command());
        Assert.Equal(BootstrapResponsibleFailureReason.BootstrapUnavailable, result.FailureReason);
        Assert.Null(result.UserId);
        Assert.Equal(existing.Id, (await db.Context.Users.AsNoTracking().SingleAsync()).Id);
        var state = await db.Initialization.GetStateAsync();
        Assert.False(state.RequiresBootstrap);
        Assert.Equal(role == UserRole.Responsable && active, state.HasResponsibleUser);
        Assert.False(state.IsInitializationComplete);
    }

    [Fact]
    public async Task Second_bootstrap_is_refused_without_changing_first_credentials()
    {
        using var db = new InitializationAdminTestDatabase();
        Assert.True((await db.Initialization.BootstrapResponsibleAsync(Command())).Success);
        var before = await db.Context.Users.AsNoTracking().SingleAsync();
        var second = await db.Initialization.BootstrapResponsibleAsync(new("Another", "another", "another secret"));
        Assert.Equal(BootstrapResponsibleFailureReason.BootstrapUnavailable, second.FailureReason);
        var after = await db.Context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(before.Id, after.Id);
        Assert.Equal(before.PasswordHash, after.PasswordHash);
    }

    [Theory]
    [InlineData("", "owner", "valid", BootstrapResponsibleFailureReason.InvalidFullName)]
    [InlineData("First", " ", "valid", BootstrapResponsibleFailureReason.InvalidUsername)]
    [InlineData("First", null, "valid", BootstrapResponsibleFailureReason.InvalidUsername)]
    [InlineData("First", "owner", "", BootstrapResponsibleFailureReason.InvalidPassword)]
    [InlineData("First", "owner", null, BootstrapResponsibleFailureReason.InvalidPassword)]
    public async Task Invalid_credentials_do_not_mutate_database(string name, string? username, string? password,
        BootstrapResponsibleFailureReason expected)
    {
        using var db = new InitializationAdminTestDatabase();
        var result = await db.Initialization.BootstrapResponsibleAsync(new(name, username!, password!));
        Assert.Equal(expected, result.FailureReason);
        await db.Context.SaveChangesAsync();
        Assert.Empty(await db.Context.Users.ToListAsync());
        Assert.True((await db.Initialization.BootstrapResponsibleAsync(Command())).Success);
    }

    [Fact]
    public async Task Cancellation_propagates_without_creating_user()
    {
        using var db = new InitializationAdminTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Initialization.BootstrapResponsibleAsync(Command(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Initialization.GetStateAsync(cancellation.Token));
        Assert.Empty(await db.Context.Users.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_independent_providers_create_only_one_initial_responsible(bool sameUsername)
    {
        using var db = new InitializationAdminTestDatabase();
        using var firstProvider = db.NewProvider();
        using var secondProvider = db.NewProvider();
        using var start = new Barrier(2);
        Task<BootstrapResponsibleResult> Attempt(ServiceProvider provider, string username) => Task.Run(async () =>
        {
            using var scope = provider.CreateScope();
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(15)));
            return await scope.ServiceProvider.GetRequiredService<IApplicationInitializationService>()
                .BootstrapResponsibleAsync(new("First", username, InitializationAdminTestDatabase.Password));
        });
        var results = await Task.WhenAll(Attempt(firstProvider, "first"), Attempt(secondProvider, sameUsername ? "first" : "second"));
        Assert.Single(results, result => result.Success);
        var loser = Assert.Single(results, result => !result.Success);
        Assert.Contains(loser.FailureReason, new[] { BootstrapResponsibleFailureReason.BootstrapUnavailable,
            BootstrapResponsibleFailureReason.ConcurrentOperation });
        var user = await db.Context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(UserRole.Responsable, user.Role);
        Assert.True(user.IsActive);
    }

    [Theory]
    [InlineData(100L, 200L)]
    [InlineData(0L, 0L)]
    [InlineData(-100L, -200L)]
    public async Task Partial_setup_resumes_explicitly_without_duplicate_user_or_opening_overwrite(long cash, long bank)
    {
        using var db = new InitializationAdminTestDatabase();
        Assert.True((await db.Initialization.BootstrapResponsibleAsync(Command())).Success);
        var partial = await db.Initialization.GetStateAsync();
        Assert.False(partial.RequiresBootstrap);
        Assert.True(partial.HasResponsibleUser);
        Assert.False(partial.HasOpeningBalances);
        Assert.False(partial.IsInitializationComplete);
        await db.LoginAsync("owner");
        var treasury = db.Get<ITreasuryService>();
        Assert.True((await treasury.CreateOpeningBalanceAsync(new(new(2026, 1, 1), cash, bank))).Success);
        Assert.True((await db.Initialization.GetStateAsync()).IsInitializationComplete);
        Assert.Equal(CreateTreasuryOpeningBalanceFailureReason.AlreadyInitialized,
            (await treasury.CreateOpeningBalanceAsync(new(new(2026, 1, 1), 999, 999))).FailureReason);
        var summary = await treasury.GetSummaryAsync();
        Assert.Equal(cash, summary.CashBalanceMinor);
        Assert.Equal(bank, summary.BankBalanceMinor);
        Assert.Single(await db.Context.Users.ToListAsync());
        Assert.Single(await db.Context.TreasuryOpeningBalances.ToListAsync());
        Assert.Empty(await db.Context.CustomerPayments.ToListAsync());
        Assert.Empty(await db.Context.SupplierPayments.ToListAsync());
        Assert.Empty(await db.Context.Expenses.ToListAsync());
        Assert.Empty(await db.Context.TreasuryTransfers.ToListAsync());
    }

    [Fact]
    public void Credential_commands_do_not_include_passwords_in_ToString()
    {
        Assert.DoesNotContain(InitializationAdminTestDatabase.Password, Command().ToString()!);
        Assert.DoesNotContain(InitializationAdminTestDatabase.Password,
            new FinancialManagement.Application.Administration.CreateEmployeeCommand("Employee", "employee", InitializationAdminTestDatabase.Password).ToString()!);
    }

    private static BootstrapResponsibleCommand Command() => new("First Responsible", "owner", InitializationAdminTestDatabase.Password);

    [Fact]
    public async Task Cancellation_after_hashing_rolls_back_and_leaves_no_pending_insert()
    {
        using var db = new InitializationAdminTestDatabase();
        using var cancellation = new CancellationTokenSource();
        using var provider = db.NewProvider(services => services.AddSingleton<IPasswordHasher>(
            new CallbackPasswordHasher(cancellation.Cancel)));
        using var scope = provider.CreateScope();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => scope.ServiceProvider
            .GetRequiredService<IApplicationInitializationService>().BootstrapResponsibleAsync(Command(), cancellation.Token));
        await scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>().SaveChangesAsync();
        Assert.Empty(await db.Context.Users.ToListAsync());
        Assert.True((await db.Initialization.BootstrapResponsibleAsync(Command())).Success);
    }
}
