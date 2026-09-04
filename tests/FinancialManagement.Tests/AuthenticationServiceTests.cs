using FinancialManagement.Application.Authentication;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class AuthenticationServiceTests
{
    private const string Username = "authentication-test-user";
    private const string Password = "Test-only password 42!";

    [Fact]
    public async Task Correct_credentials_authenticate_and_fill_session()
    {
        using var database = new AuthenticationTestDatabase();
        var user = database.AddUser(Username, Password, UserRole.Responsable);

        Assert.False(database.Session.IsAuthenticated);

        var result = await database.AuthenticationService.AuthenticateAsync(Username, Password);

        Assert.True(result.Success);
        Assert.NotNull(result.User);
        Assert.Equal(user.Id, result.User.UserId);
        Assert.Equal(user.FullName, result.User.FullName);
        Assert.Equal(user.Username, result.User.Username);
        Assert.Equal(user.Role, result.User.Role);
        Assert.True(database.Session.IsAuthenticated);
        Assert.Equal(user.Id, database.Session.UserId);
        Assert.Equal(user.FullName, database.Session.FullName);
        Assert.Equal(user.Username, database.Session.Username);
        Assert.Equal(user.Role, database.Session.Role);
    }

    [Fact]
    public async Task Incorrect_password_is_rejected()
    {
        using var database = new AuthenticationTestDatabase();
        database.AddUser(Username, Password);

        var result = await database.AuthenticationService.AuthenticateAsync(
            Username,
            "Incorrect test password");

        AssertAuthenticationFailed(result, database.Session);
    }

    [Fact]
    public async Task Unknown_username_is_rejected()
    {
        using var database = new AuthenticationTestDatabase();

        var result = await database.AuthenticationService.AuthenticateAsync(
            "unknown-authentication-test-user",
            Password);

        AssertAuthenticationFailed(result, database.Session);
    }

    [Fact]
    public async Task Inactive_user_is_rejected_with_correct_password()
    {
        using var database = new AuthenticationTestDatabase();
        database.AddUser(Username, Password, isActive: false);

        var result = await database.AuthenticationService.AuthenticateAsync(Username, Password);

        AssertAuthenticationFailed(result, database.Session);
    }

    [Theory]
    [InlineData("", Password)]
    [InlineData("   ", Password)]
    [InlineData(Username, "")]
    public async Task Invalid_empty_credentials_are_rejected_without_exception(
        string username,
        string password)
    {
        using var database = new AuthenticationTestDatabase();

        var result = await database.AuthenticationService.AuthenticateAsync(username, password);

        AssertAuthenticationFailed(result, database.Session);
    }

    [Fact]
    public async Task Logout_clears_authenticated_session()
    {
        using var database = new AuthenticationTestDatabase();
        database.AddUser(Username, Password, UserRole.Employe);
        var result = await database.AuthenticationService.AuthenticateAsync(Username, Password);
        Assert.True(result.Success);

        database.AuthenticationService.Logout();

        AssertSessionIsEmpty(database.Session);
    }

    [Fact]
    public async Task Failed_authentication_clears_a_previous_session()
    {
        using var database = new AuthenticationTestDatabase();
        database.AddUser(Username, Password);
        var successfulResult = await database.AuthenticationService.AuthenticateAsync(
            Username,
            Password);
        Assert.True(successfulResult.Success);

        var failedResult = await database.AuthenticationService.AuthenticateAsync(
            Username,
            "Incorrect test password");

        AssertAuthenticationFailed(failedResult, database.Session);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-password-hash")]
    [InlineData("AQIDBA==")]
    public async Task Invalid_stored_password_hash_is_a_safe_generic_failure(string invalidHash)
    {
        using var database = new AuthenticationTestDatabase();
        var user = database.AddUser(Username, Password);
        database.SetPasswordHash(user, invalidHash);

        var exception = await Record.ExceptionAsync(() =>
            database.AuthenticationService.AuthenticateAsync(Username, Password));

        Assert.Null(exception);
        AssertSessionIsEmpty(database.Session);
    }

    private static void AssertAuthenticationFailed(
        AuthenticationResult result,
        ICurrentUserSession session)
    {
        Assert.False(result.Success);
        Assert.Null(result.User);
        AssertSessionIsEmpty(session);
    }

    private static void AssertSessionIsEmpty(ICurrentUserSession session)
    {
        Assert.False(session.IsAuthenticated);
        Assert.Null(session.UserId);
        Assert.Null(session.FullName);
        Assert.Null(session.Username);
        Assert.Null(session.Role);
    }

    private sealed class AuthenticationTestDatabase : IDisposable
    {
        private static readonly DateTimeOffset TestTimestamp =
            new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly string _databasePath;
        private readonly ServiceProvider _serviceProvider;
        private readonly IServiceScope _scope;

        public AuthenticationTestDatabase()
        {
            _databasePath = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-Authentication-{Guid.NewGuid():N}.db");
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
            Session = _scope.ServiceProvider.GetRequiredService<ICurrentUserSession>();
        }

        private FinancialManagementDbContext Context { get; }

        private IPasswordHasher PasswordHasher { get; }

        public IAuthenticationService AuthenticationService { get; }

        public ICurrentUserSession Session { get; }

        public User AddUser(
            string username,
            string password,
            UserRole role = UserRole.Employe,
            bool isActive = true)
        {
            var user = new User
            {
                FullName = "Authentication Test User",
                Username = username,
                PasswordHash = PasswordHasher.HashPassword(password),
                Role = role,
                IsActive = isActive,
                CreatedAtUtc = TestTimestamp,
                UpdatedAtUtc = TestTimestamp
            };

            Context.Users.Add(user);
            Context.SaveChanges();
            return user;
        }

        public void SetPasswordHash(User user, string passwordHash)
        {
            user.PasswordHash = passwordHash;
            Context.SaveChanges();
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
