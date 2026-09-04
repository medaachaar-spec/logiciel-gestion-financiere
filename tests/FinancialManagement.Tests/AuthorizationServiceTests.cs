using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class AuthorizationServiceTests
{
    private const string Password = "Authorization test password 42!";

    [Fact]
    public async Task Unauthenticated_session_is_denied_every_permission_and_capability()
    {
        using var database = new AuthorizationTestDatabase();

        foreach (var module in Enum.GetValues<PermissionModule>())
        {
            await AssertPermissionsAsync(database.AuthorizationService, module, false, false, false);
        }

        foreach (var capability in Enum.GetValues<ResponsibleCapability>())
        {
            Assert.False(database.AuthorizationService.CanAccessResponsibleCapability(capability));
        }
    }

    [Fact]
    public async Task Responsable_has_all_module_permissions_without_database_rows()
    {
        using var database = new AuthorizationTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        foreach (var module in Enum.GetValues<PermissionModule>())
        {
            await AssertPermissionsAsync(database.AuthorizationService, module, true, true, true);
        }
    }

    [Fact]
    public async Task Responsable_has_every_responsible_capability()
    {
        using var database = new AuthorizationTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        foreach (var capability in Enum.GetValues<ResponsibleCapability>())
        {
            Assert.True(database.AuthorizationService.CanAccessResponsibleCapability(capability));
        }
    }

    [Fact]
    public async Task Employee_without_permission_row_is_denied_by_default()
    {
        using var database = new AuthorizationTestDatabase();
        var employee = database.AddUser("employee", UserRole.Employe);
        await database.AuthenticateAsync(employee);

        foreach (var module in Enum.GetValues<PermissionModule>())
        {
            await AssertPermissionsAsync(database.AuthorizationService, module, false, false, false);
        }
    }

    [Fact]
    public async Task Employee_with_all_flags_is_granted_all_module_actions()
    {
        using var database = new AuthorizationTestDatabase();
        var employee = database.AddUser("employee", UserRole.Employe);
        database.AddPermission(employee, PermissionModule.Sales, true, true, true);
        await database.AuthenticateAsync(employee);

        await AssertPermissionsAsync(
            database.AuthorizationService,
            PermissionModule.Sales,
            true,
            true,
            true);
    }

    [Fact]
    public async Task Employee_with_view_only_is_denied_create_and_modify()
    {
        using var database = new AuthorizationTestDatabase();
        var employee = database.AddUser("employee", UserRole.Employe);
        database.AddPermission(employee, PermissionModule.Clients, true, false, false);
        await database.AuthenticateAsync(employee);

        await AssertPermissionsAsync(
            database.AuthorizationService,
            PermissionModule.Clients,
            true,
            false,
            false);
    }

    [Fact]
    public async Task View_false_neutralizes_inconsistent_create_and_modify_flags()
    {
        using var database = new AuthorizationTestDatabase();
        var employee = database.AddUser("employee", UserRole.Employe);
        database.AddPermission(employee, PermissionModule.Expenses, false, true, true);
        await database.AuthenticateAsync(employee);

        await AssertPermissionsAsync(
            database.AuthorizationService,
            PermissionModule.Expenses,
            false,
            false,
            false);
    }

    [Fact]
    public async Task Employee_permissions_are_isolated_by_module()
    {
        using var database = new AuthorizationTestDatabase();
        var employee = database.AddUser("employee", UserRole.Employe);
        database.AddPermission(employee, PermissionModule.Sales, true, true, false);
        await database.AuthenticateAsync(employee);

        await AssertPermissionsAsync(
            database.AuthorizationService,
            PermissionModule.Sales,
            true,
            true,
            false);
        await AssertPermissionsAsync(
            database.AuthorizationService,
            PermissionModule.Purchases,
            false,
            false,
            false);
    }

    [Fact]
    public async Task Employee_permissions_are_isolated_by_user()
    {
        using var database = new AuthorizationTestDatabase();
        var employeeA = database.AddUser("employee-a", UserRole.Employe);
        var employeeB = database.AddUser("employee-b", UserRole.Employe);
        database.AddPermission(employeeA, PermissionModule.SupplierPayments, true, true, true);
        await database.AuthenticateAsync(employeeB);

        await AssertPermissionsAsync(
            database.AuthorizationService,
            PermissionModule.SupplierPayments,
            false,
            false,
            false);
    }

    [Fact]
    public async Task Employee_is_denied_every_responsible_capability()
    {
        using var database = new AuthorizationTestDatabase();
        var employee = database.AddUser("employee", UserRole.Employe);
        await database.AuthenticateAsync(employee);

        foreach (var capability in Enum.GetValues<ResponsibleCapability>())
        {
            Assert.False(database.AuthorizationService.CanAccessResponsibleCapability(capability));
        }
    }

    [Fact]
    public async Task Logout_and_user_change_do_not_reuse_previous_permissions()
    {
        using var database = new AuthorizationTestDatabase();
        var employeeA = database.AddUser("employee-a", UserRole.Employe);
        var employeeB = database.AddUser("employee-b", UserRole.Employe);
        database.AddPermission(employeeA, PermissionModule.Sales, true, true, true);

        await database.AuthenticateAsync(employeeA);
        await AssertPermissionsAsync(
            database.AuthorizationService,
            PermissionModule.Sales,
            true,
            true,
            true);

        database.AuthenticationService.Logout();
        await AssertPermissionsAsync(
            database.AuthorizationService,
            PermissionModule.Sales,
            false,
            false,
            false);

        await database.AuthenticateAsync(employeeB);
        await AssertPermissionsAsync(
            database.AuthorizationService,
            PermissionModule.Sales,
            false,
            false,
            false);
    }

    [Fact]
    public async Task Authentication_authorization_logout_and_responsable_flow_is_consistent()
    {
        using var database = new AuthorizationTestDatabase();
        var employee = database.AddUser("flow-employee", UserRole.Employe);
        database.AddPermission(employee, PermissionModule.Sales, true, true, false);

        Assert.False(database.Session.IsAuthenticated);

        await database.AuthenticateAsync(employee);
        Assert.True(await database.AuthorizationService.CanAsync(
            PermissionModule.Sales,
            PermissionAction.View));
        Assert.True(await database.AuthorizationService.CanAsync(
            PermissionModule.Sales,
            PermissionAction.Create));
        Assert.False(await database.AuthorizationService.CanAsync(
            PermissionModule.Purchases,
            PermissionAction.View));

        database.AuthenticationService.Logout();
        Assert.False(await database.AuthorizationService.CanAsync(
            PermissionModule.Sales,
            PermissionAction.View));

        var responsible = database.AddUser("flow-responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);
        Assert.True(await database.AuthorizationService.CanAsync(
            PermissionModule.Purchases,
            PermissionAction.Modify));
        Assert.True(database.AuthorizationService.CanAccessResponsibleCapability(
            ResponsibleCapability.Treasury));
    }

    [Fact]
    public async Task Undefined_permission_values_are_denied_defensively()
    {
        using var database = new AuthorizationTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        Assert.False(await database.AuthorizationService.CanAsync(
            (PermissionModule)999,
            (PermissionAction)999));
        var effectivePermissions = await database.AuthorizationService
            .GetEffectivePermissionsAsync((PermissionModule)999);
        Assert.False(effectivePermissions.CanView);
        Assert.False(effectivePermissions.CanCreate);
        Assert.False(effectivePermissions.CanModify);
        Assert.False(database.AuthorizationService.CanAccessResponsibleCapability(
            (ResponsibleCapability)999));
    }

    [Fact]
    public void PermissionAction_contains_only_validated_actions()
    {
        Assert.Equal(
            [PermissionAction.View, PermissionAction.Create, PermissionAction.Modify],
            Enum.GetValues<PermissionAction>());
    }

    [Fact]
    public void ResponsibleCapability_contains_only_validated_capabilities()
    {
        Assert.Equal(
            [
                ResponsibleCapability.GlobalDashboard,
                ResponsibleCapability.Treasury,
                ResponsibleCapability.Administration,
                ResponsibleCapability.BackupRestore,
                ResponsibleCapability.PermanentDelete
            ],
            Enum.GetValues<ResponsibleCapability>());
    }

    private static async Task AssertPermissionsAsync(
        IAuthorizationService authorizationService,
        PermissionModule module,
        bool canView,
        bool canCreate,
        bool canModify)
    {
        var effectivePermissions = await authorizationService
            .GetEffectivePermissionsAsync(module);

        Assert.Equal(canView, effectivePermissions.CanView);
        Assert.Equal(canCreate, effectivePermissions.CanCreate);
        Assert.Equal(canModify, effectivePermissions.CanModify);
        Assert.Equal(
            canView,
            await authorizationService.CanAsync(module, PermissionAction.View));
        Assert.Equal(
            canCreate,
            await authorizationService.CanAsync(module, PermissionAction.Create));
        Assert.Equal(
            canModify,
            await authorizationService.CanAsync(module, PermissionAction.Modify));
    }

    private sealed class AuthorizationTestDatabase : IDisposable
    {
        private static readonly DateTimeOffset TestTimestamp =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _scope;

        public AuthorizationTestDatabase()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-Authorization-{Guid.NewGuid():N}.db");
            _serviceProvider = new ServiceCollection()
                .AddInfrastructure($"Data Source={_databasePath};Pooling=False")
                .BuildServiceProvider(new ServiceProviderOptions
                {
                    ValidateOnBuild = true,
                    ValidateScopes = true
                });
            _scope = _serviceProvider.CreateScope();
            Context = _scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
            Context.Database.EnsureCreated();
            PasswordHasher = _scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            AuthenticationService = _scope.ServiceProvider
                .GetRequiredService<IAuthenticationService>();
            AuthorizationService = _scope.ServiceProvider
                .GetRequiredService<IAuthorizationService>();
            Session = _scope.ServiceProvider.GetRequiredService<ICurrentUserSession>();
        }

        private FinancialManagementDbContext Context { get; }

        private IPasswordHasher PasswordHasher { get; }

        public IAuthenticationService AuthenticationService { get; }

        public IAuthorizationService AuthorizationService { get; }

        public ICurrentUserSession Session { get; }

        public User AddUser(string username, UserRole role)
        {
            var user = new User
            {
                FullName = $"Authorization Test {username}",
                Username = username,
                PasswordHash = PasswordHasher.HashPassword(Password),
                Role = role,
                IsActive = true,
                CreatedAtUtc = TestTimestamp,
                UpdatedAtUtc = TestTimestamp
            };

            Context.Users.Add(user);
            Context.SaveChanges();
            return user;
        }

        public void AddPermission(
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
}
