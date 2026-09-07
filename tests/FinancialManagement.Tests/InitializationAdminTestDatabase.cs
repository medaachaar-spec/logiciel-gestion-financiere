using FinancialManagement.Application.Administration;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Initialization;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

internal sealed class InitializationAdminTestDatabase : IDisposable
{
    internal const string Password = "Test-only initialization credential 42!";
    internal static readonly DateTimeOffset Now = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"FinancialManagement-Initialization-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    internal InitializationAdminTestDatabase()
    {
        _provider = NewProvider();
        _scope = _provider.CreateScope();
        Context = Get<FinancialManagementDbContext>();
        Context.Database.Migrate();
    }

    internal FinancialManagementDbContext Context { get; }
    internal IApplicationInitializationService Initialization => Get<IApplicationInitializationService>();
    internal IUserAdministrationService Administration => Get<IUserAdministrationService>();
    internal T Get<T>() where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();
    internal ServiceProvider NewProvider(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider());
        services.AddInfrastructure($"Data Source={_path};Pooling=False");
        configure?.Invoke(services);
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    internal User AddUser(string username = "owner", UserRole role = UserRole.Responsable, bool active = true)
    {
        var user = new User
        {
            FullName = username, Username = username,
            PasswordHash = Get<IPasswordHasher>().HashPassword(Password), Role = role,
            IsActive = active, CreatedAtUtc = Now, UpdatedAtUtc = Now
        };
        Context.Users.Add(user);
        Context.SaveChanges();
        Context.Entry(user).State = EntityState.Detached;
        return user;
    }

    internal async Task<User> LoginResponsibleAsync()
    {
        var user = AddUser();
        await LoginAsync(user.Username);
        return user;
    }

    internal async Task LoginAsync(string username) => Assert.True(
        (await Get<IAuthenticationService>().AuthenticateAsync(username, Password)).Success);

    public void Dispose()
    {
        _scope.Dispose();
        _provider.Dispose();
        foreach (var path in new[] { _path, _path + "-wal", _path + "-shm", _path + "-journal" })
            if (File.Exists(path)) File.Delete(path);
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
