using FinancialManagement.Application.Administration;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class UserAdministrationServiceTests
{
    [Fact]
    public async Task Responsible_creates_employee_with_no_permissions_and_canonical_hash()
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        var result = await db.Administration.CreateEmployeeAsync(Command());
        Assert.True(result.Success);
        var user = await db.Context.Users.AsNoTracking().SingleAsync(item => item.Id == result.UserId);
        Assert.Equal(UserRole.Employe, user.Role);
        Assert.True(user.IsActive);
        Assert.Equal("Employee", user.FullName);
        Assert.True(db.Get<IPasswordHasher>().VerifyPassword(user.PasswordHash, InitializationAdminTestDatabase.Password));
        Assert.NotEqual(InitializationAdminTestDatabase.Password, user.PasswordHash);
        Assert.Equal(InitializationAdminTestDatabase.Now, user.CreatedAtUtc);
        Assert.Empty(await db.Context.UserPermissions.ToListAsync());
        var listing = await db.Administration.GetUsersAsync();
        Assert.True(listing.Success);
        var item = Assert.Single(listing.Items, item => item.Id == user.Id);
        Assert.Equal(7, item.Permissions.Count);
        Assert.All(item.Permissions, permission => Assert.False(permission.CanView || permission.CanCreate || permission.CanModify));
        await db.LoginAsync("employee");
        Assert.False(await db.Get<IAuthorizationService>().CanAsync(PermissionModule.Sales, PermissionAction.View));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unauthenticated_and_employee_cannot_administer(bool loginEmployee)
    {
        using var db = new InitializationAdminTestDatabase();
        var employee = db.AddUser("employee", UserRole.Employe);
        if (loginEmployee) await db.LoginAsync(employee.Username);
        var expected = loginEmployee ? UserAdministrationFailureReason.Forbidden : UserAdministrationFailureReason.NotAuthenticated;
        Assert.Equal(expected, (await db.Administration.CreateEmployeeAsync(Command())).FailureReason);
        Assert.Equal(expected, (await db.Administration.SetActiveAsync(employee.Id, false)).FailureReason);
        Assert.Equal(expected, (await db.Administration.SetPermissionAsync(employee.Id, new(PermissionModule.Sales, true, true, true))).FailureReason);
        Assert.Equal(expected, (await db.Administration.ResetPasswordAsync(employee.Id, "new-secret")).FailureReason);
        var listing = await db.Administration.GetUsersAsync();
        Assert.Equal(expected, listing.FailureReason);
        Assert.Empty(listing.Items);
        var unchanged = await db.Context.Users.AsNoTracking().SingleAsync();
        Assert.True(unchanged.IsActive);
        Assert.Equal(employee.PasswordHash, unchanged.PasswordHash);
        Assert.Empty(await db.Context.UserPermissions.ToListAsync());
    }

    [Theory]
    [InlineData(PermissionModule.Sales)]
    [InlineData(PermissionModule.Purchases)]
    [InlineData(PermissionModule.Expenses)]
    [InlineData(PermissionModule.Clients)]
    [InlineData(PermissionModule.Suppliers)]
    [InlineData(PermissionModule.CustomerPayments)]
    [InlineData(PermissionModule.SupplierPayments)]
    public async Task Permissions_are_configurable_and_View_off_neutralizes_create_modify(PermissionModule module)
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        var employee = await db.Administration.CreateEmployeeAsync(Command());
        var id = employee.UserId!.Value;
        Assert.True((await db.Administration.SetPermissionAsync(id, new(module, true, true, true))).Success);
        await db.LoginAsync("employee");
        var authorization = db.Get<IAuthorizationService>();
        Assert.True(await authorization.CanAsync(module, PermissionAction.View));
        Assert.True(await authorization.CanAsync(module, PermissionAction.Create));
        Assert.True(await authorization.CanAsync(module, PermissionAction.Modify));
        foreach (var capability in Enum.GetValues<ResponsibleCapability>())
            Assert.False(authorization.CanAccessResponsibleCapability(capability));
        await db.LoginAsync("owner");
        Assert.True((await db.Administration.SetPermissionAsync(id, new(module, false, true, true))).Success);
        var stored = await db.Context.UserPermissions.AsNoTracking().SingleAsync();
        Assert.False(stored.CanView || stored.CanCreate || stored.CanModify);
        await db.LoginAsync("employee");
        Assert.False(await authorization.CanAsync(module, PermissionAction.Create));
        Assert.False(await authorization.CanAsync(module, PermissionAction.Modify));
    }

    [Fact]
    public async Task Deactivate_reactivate_preserves_identity_and_authentication_honors_activity()
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        var employee = await db.Administration.CreateEmployeeAsync(Command());
        var id = employee.UserId!.Value;
        Assert.True((await db.Administration.SetActiveAsync(id, false)).Success);
        Assert.False((await db.Get<IAuthenticationService>().AuthenticateAsync("employee", InitializationAdminTestDatabase.Password)).Success);
        Assert.False(db.Get<ICurrentUserSession>().IsAuthenticated);
        await db.LoginAsync("owner");
        Assert.True((await db.Administration.SetActiveAsync(id, true)).Success);
        await db.LoginAsync("employee");
        Assert.Equal(id, db.Get<ICurrentUserSession>().UserId);
        Assert.Equal(2, await db.Context.Users.CountAsync());
    }

    [Fact]
    public async Task Reset_uses_canonical_hash_and_old_password_stops_working()
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        var employee = await db.Administration.CreateEmployeeAsync(Command());
        const string newPassword = "Test-only replacement password 84!";
        Assert.True((await db.Administration.ResetPasswordAsync(employee.UserId!.Value, newPassword)).Success);
        Assert.False((await db.Get<IAuthenticationService>().AuthenticateAsync("employee", InitializationAdminTestDatabase.Password)).Success);
        Assert.True((await db.Get<IAuthenticationService>().AuthenticateAsync("employee", newPassword)).Success);
        var hash = await db.Context.Users.Where(user => user.Id == employee.UserId).Select(user => user.PasswordHash).SingleAsync();
        Assert.True(db.Get<IPasswordHasher>().VerifyPassword(hash, newPassword));
        Assert.NotEqual(newPassword, hash);
    }

    [Fact]
    public async Task Duplicate_username_including_inactive_user_is_rejected_without_change()
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        var employee = db.AddUser("employee", UserRole.Employe, false);
        Assert.Equal(UserAdministrationFailureReason.DuplicateUsername,
            (await db.Administration.CreateEmployeeAsync(Command())).FailureReason);
        Assert.Equal(2, await db.Context.Users.CountAsync());
        var preserved = await db.Context.Users.AsNoTracking().SingleAsync(user => user.Id == employee.Id);
        Assert.False(preserved.IsActive);
        Assert.Equal(employee.PasswordHash, preserved.PasswordHash);
    }

    [Theory]
    [InlineData(" ", "employee", "valid", UserAdministrationFailureReason.InvalidFullName)]
    [InlineData("Employee", " ", "valid", UserAdministrationFailureReason.InvalidUsername)]
    [InlineData("Employee", "employee", "", UserAdministrationFailureReason.InvalidPassword)]
    public async Task Invalid_employee_input_is_rejected_without_writes(string name, string username, string password,
        UserAdministrationFailureReason expected)
    {
        using var db = new InitializationAdminTestDatabase();
        await db.LoginResponsibleAsync();
        Assert.Equal(expected, (await db.Administration.CreateEmployeeAsync(new(name, username, password))).FailureReason);
        await db.Context.SaveChangesAsync();
        Assert.Single(await db.Context.Users.ToListAsync());
    }

    [Fact]
    public async Task Unknown_users_invalid_modules_and_responsible_permissions_are_rejected()
    {
        using var db = new InitializationAdminTestDatabase();
        var owner = await db.LoginResponsibleAsync();
        Assert.Equal(UserAdministrationFailureReason.UserNotFound, (await db.Administration.SetActiveAsync(999, false)).FailureReason);
        Assert.Equal(UserAdministrationFailureReason.UserNotFound, (await db.Administration.ResetPasswordAsync(999, "valid")).FailureReason);
        Assert.Equal(UserAdministrationFailureReason.UserNotFound,
            (await db.Administration.SetPermissionAsync(999, new(PermissionModule.Sales, true, true, true))).FailureReason);
        Assert.Equal(UserAdministrationFailureReason.EmployeeRequired,
            (await db.Administration.SetPermissionAsync(owner.Id, new(PermissionModule.Sales, true, true, true))).FailureReason);
        Assert.Equal(UserAdministrationFailureReason.InvalidModule,
            (await db.Administration.SetPermissionAsync(owner.Id, new((PermissionModule)999, true, true, true))).FailureReason);
        Assert.Equal(UserAdministrationFailureReason.InvalidPassword, (await db.Administration.ResetPasswordAsync(owner.Id, "")).FailureReason);
        Assert.Empty(await db.Context.UserPermissions.ToListAsync());
    }

    [Fact]
    public async Task Last_active_responsible_cannot_be_deactivated()
    {
        using var db = new InitializationAdminTestDatabase();
        var owner = await db.LoginResponsibleAsync();
        Assert.Equal(UserAdministrationFailureReason.LastActiveResponsible,
            (await db.Administration.SetActiveAsync(owner.Id, false)).FailureReason);
        Assert.True((await db.Context.Users.AsNoTracking().SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Stale_responsible_session_cannot_administer_after_persisted_deactivation()
    {
        using var db = new InitializationAdminTestDatabase();
        var owner = await db.LoginResponsibleAsync();
        await db.Context.Users.Where(user => user.Id == owner.Id).ExecuteUpdateAsync(setters => setters.SetProperty(user => user.IsActive, false));
        Assert.True(db.Get<ICurrentUserSession>().IsAuthenticated);
        Assert.Equal(UserAdministrationFailureReason.Forbidden, (await db.Administration.CreateEmployeeAsync(Command())).FailureReason);
        Assert.Equal(UserAdministrationFailureReason.Forbidden, (await db.Administration.GetUsersAsync()).FailureReason);
        Assert.Single(await db.Context.Users.ToListAsync());
    }

    [Fact]
    public async Task Concurrent_employee_creations_with_same_username_return_controlled_results()
    {
        using var db = new InitializationAdminTestDatabase();
        db.AddUser();
        using var first = db.NewProvider();
        using var second = db.NewProvider();
        using var start = new Barrier(2);
        Task<UserAdministrationResult> Attempt(ServiceProvider provider) => Task.Run(async () =>
        {
            using var scope = provider.CreateScope();
            Assert.True((await scope.ServiceProvider.GetRequiredService<IAuthenticationService>()
                .AuthenticateAsync("owner", InitializationAdminTestDatabase.Password)).Success);
            Assert.True(start.SignalAndWait(TimeSpan.FromSeconds(15)));
            return await scope.ServiceProvider.GetRequiredService<IUserAdministrationService>().CreateEmployeeAsync(Command());
        });
        var results = await Task.WhenAll(Attempt(first), Attempt(second));
        Assert.Single(results, result => result.Success);
        Assert.Contains(Assert.Single(results, result => !result.Success).FailureReason,
            new[] { UserAdministrationFailureReason.DuplicateUsername, UserAdministrationFailureReason.ConcurrentOperation });
        Assert.Equal(2, await db.Context.Users.CountAsync());
    }

    [Fact]
    public async Task Cancellation_leaves_administration_unchanged()
    {
        using var db = new InitializationAdminTestDatabase();
        var owner = await db.LoginResponsibleAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Administration.CreateEmployeeAsync(Command(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Administration.SetActiveAsync(owner.Id, false, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Administration.ResetPasswordAsync(owner.Id, "new", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Administration.SetPermissionAsync(owner.Id, new(PermissionModule.Sales, true, true, true), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Administration.GetUsersAsync(cancellation.Token));
        Assert.Single(await db.Context.Users.ToListAsync());
        Assert.Empty(await db.Context.UserPermissions.ToListAsync());
    }

    [Fact]
    public void Public_administration_contract_exposes_no_deletion_role_creation_or_password_hash()
    {
        Assert.Equal(new[] { "CreateEmployeeAsync", "GetUsersAsync", "ResetPasswordAsync", "SetActiveAsync", "SetPermissionAsync" },
            typeof(IUserAdministrationService).GetMethods().Select(method => method.Name).Order().ToArray());
        Assert.DoesNotContain(typeof(UserAdministrationItem).GetProperties(), property => property.Name.Contains("Password"));
    }

    private static CreateEmployeeCommand Command() => new("Employee", "employee", InitializationAdminTestDatabase.Password);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Logout_during_credential_hashing_rolls_back_the_entire_operation(bool reset)
    {
        using var db = new InitializationAdminTestDatabase();
        var owner = db.AddUser();
        Action logout = () => { };
        using var provider = db.NewProvider(services => services.AddSingleton<IPasswordHasher>(
            new CallbackPasswordHasher(() => logout())));
        using var scope = provider.CreateScope();
        var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        Assert.True((await authentication.AuthenticateAsync("owner", InitializationAdminTestDatabase.Password)).Success);
        logout = authentication.Logout;
        var administration = scope.ServiceProvider.GetRequiredService<IUserAdministrationService>();
        var result = reset
            ? await administration.ResetPasswordAsync(owner.Id, "replacement")
            : await administration.CreateEmployeeAsync(Command());
        Assert.Equal(UserAdministrationFailureReason.NotAuthenticated, result.FailureReason);
        await scope.ServiceProvider.GetRequiredService<FinancialManagement.Infrastructure.FinancialManagementDbContext>().SaveChangesAsync();
        var unchanged = await db.Context.Users.AsNoTracking().SingleAsync();
        Assert.Equal(owner.PasswordHash, unchanged.PasswordHash);
    }
}
