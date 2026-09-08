using System.Data.Common;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Application.Sales;
using FinancialManagement.Application.Purchases;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

internal sealed class DashboardSnapshotTestDatabase : IAsyncDisposable
{
    private const string Password = "Snapshot regression credential 42!";
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"FinancialManagement-Snapshot-{Guid.NewGuid():N}.db");
    private readonly ServiceProvider _readerProvider;
    private readonly ServiceProvider _writerProvider;
    private readonly IServiceScope _readerScope;
    private readonly IServiceScope _writerScope;

    private DashboardSnapshotTestDatabase()
    {
        _readerProvider = Provider(Trace);
        _writerProvider = Provider(null);
        _readerScope = _readerProvider.CreateScope();
        _writerScope = _writerProvider.CreateScope();
        Context = _readerScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        WriterContext = _writerScope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
    }

    internal SnapshotTrace Trace { get; } = new();
    internal FinancialManagementDbContext Context { get; }
    internal FinancialManagementDbContext WriterContext { get; }
    internal long ClientId { get; private set; }
    internal long SupplierId { get; private set; }
    internal IFinancialDashboardService Dashboard => Get<IFinancialDashboardService>();
    internal ITreasuryService Treasury => Get<ITreasuryService>();
    internal T Get<T>() where T : notnull => _readerScope.ServiceProvider.GetRequiredService<T>();
    internal T Writer<T>() where T : notnull => _writerScope.ServiceProvider.GetRequiredService<T>();

    internal static async Task<DashboardSnapshotTestDatabase> CreateAsync()
    {
        var db = new DashboardSnapshotTestDatabase();
        try
        {
            await db.Context.Database.MigrateAsync();
            var now = DateTimeOffset.UtcNow;
            db.Context.Users.Add(new User
            {
                Username = "snapshot-owner", FullName = "Snapshot Owner", Role = UserRole.Responsable,
                IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now,
                PasswordHash = db.Get<IPasswordHasher>().HashPassword(Password)
            });
            var client = new Client { Name = "Snapshot client", CreatedAtUtc = now, UpdatedAtUtc = now };
            var supplier = new Supplier { Name = "Snapshot supplier", CreatedAtUtc = now, UpdatedAtUtc = now };
            db.Context.AddRange(client, supplier);
            await db.Context.SaveChangesAsync();
            db.ClientId = client.Id;
            db.SupplierId = supplier.Id;
            foreach (var scope in new[] { db._readerScope, db._writerScope })
                Assert.True((await scope.ServiceProvider.GetRequiredService<IAuthenticationService>()
                    .AuthenticateAsync("snapshot-owner", Password)).Success);
            db.Context.ChangeTracker.Clear();
            return db;
        }
        catch
        {
            await db.DisposeAsync();
            throw;
        }
    }

    internal async Task CreateSaleAsync(long amount = 10000, long payment = 4000) =>
        Assert.True((await Writer<ISaleService>().CreateAsync(
            new(ClientId, amount, null, null, new(payment, PaymentMethod.Cash, null)))).Success);

    internal async Task CreatePurchaseAsync() =>
        Assert.True((await Writer<IPurchaseService>().CreateAsync(
            new(SupplierId, 10000, null, null, new(4000, PaymentMethod.Cash, null)))).Success);

    private ServiceProvider Provider(SnapshotTrace? trace)
    {
        var services = new ServiceCollection();
        // Bound failure time if a regression mistakenly reserves a writer lock.
        services.AddInfrastructure($"Data Source={_path};Pooling=False;Default Timeout=2");
        if (trace is not null)
            services.AddDbContext<FinancialManagementDbContext>(options => options.AddInterceptors(trace));
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
    }

    public async ValueTask DisposeAsync()
    {
        _readerScope.Dispose();
        _writerScope.Dispose();
        await _readerProvider.DisposeAsync();
        await _writerProvider.DisposeAsync();
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal" })
            if (File.Exists(_path + suffix)) File.Delete(_path + suffix);
    }
}

internal sealed class SnapshotTrace : DbCommandInterceptor, IDbConnectionInterceptor
{
    internal sealed record Read(string Sql, DbConnection Connection, DbTransaction? Transaction);
    internal List<Read> Reads { get; } = [];
    internal Func<int, DbCommand, CancellationToken, Task>? BeforeRead { get; set; }
    internal bool Enabled { get; private set; }
    internal int Opens { get; private set; }
    internal int Closes { get; private set; }

    internal void Start(Func<int, DbCommand, CancellationToken, Task>? beforeRead = null)
    {
        Reads.Clear();
        Opens = Closes = 0;
        Enabled = true;
        BeforeRead = beforeRead;
    }

    public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default)
    {
        if (Enabled)
        {
            Reads.Add(new Read(command.CommandText, command.Connection!, command.Transaction));
            if (BeforeRead is { } callback) await callback(Reads.Count, command, cancellationToken);
        }
        return result;
    }

    public Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (Enabled) Opens++;
        return Task.CompletedTask;
    }

    public Task ConnectionClosedAsync(DbConnection connection, ConnectionEndEventData eventData)
    {
        if (Enabled) Closes++;
        return Task.CompletedTask;
    }
}
