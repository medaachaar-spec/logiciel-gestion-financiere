using System.Security.Cryptography;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Backup;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using FinancialManagement.Infrastructure.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class DatabaseBackupServiceTests
{
    private static readonly DateTimeOffset TestUtc = new(2026, 9, 4, 12, 30, 45, TimeSpan.Zero);

    [Fact]
    public async Task Operations_RequireAnAuthenticatedResponsable()
    {
        await using var fixture = await BackupFixture.CreateAsync();

        var unauthenticated = await fixture.Service.CreateLocalBackupAsync();
        Assert.False(unauthenticated.Success);
        Assert.Equal(DatabaseBackupFailureReason.NotAuthenticated, unauthenticated.FailureReason);

        await fixture.AddEmployeeWithEveryPermissionAsync();
        await fixture.AuthenticateAsync("employee", "Password-123!");

        var employeeBackup = await fixture.Service.CreateLocalBackupAsync();
        var employeeRestore = await fixture.Service.RestoreAsync(new(fixture.LivePath));
        Assert.Equal(DatabaseBackupFailureReason.Forbidden, employeeBackup.FailureReason);
        Assert.Equal(DatabaseBackupFailureReason.Forbidden, employeeRestore.FailureReason);
    }

    [Fact]
    public async Task LocalAndExternalBackups_UseDistinctNamesAndRespectDestinationRules()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        Assert.False(Directory.Exists(fixture.BackupDirectory));

        var first = await fixture.Service.CreateLocalBackupAsync();
        var second = await fixture.Service.CreateLocalBackupAsync();

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEqual(first.BackupPath, second.BackupPath);
        Assert.True(File.Exists(first.BackupPath));
        Assert.True(first.FileSizeBytes > 0);
        Assert.Equal(TestUtc, first.CreatedAtUtc);
        Assert.StartsWith("FinancialManagement_Backup_20260904_123045_000_", first.FileName);

        var externalDirectory = Directory.CreateDirectory(Path.Combine(fixture.Root, "external")).FullName;
        var external = await fixture.Service.CreateExternalBackupAsync(new(externalDirectory));
        Assert.True(external.Success);
        Assert.Equal(externalDirectory, Path.GetDirectoryName(external.BackupPath));

        var missingDirectory = Path.Combine(fixture.Root, "missing-external");
        var missing = await fixture.Service.CreateExternalBackupAsync(new(missingDirectory));
        Assert.Equal(DatabaseBackupFailureReason.DestinationDirectoryNotFound, missing.FailureReason);
        Assert.False(Directory.Exists(missingDirectory));
    }

    [Fact]
    public async Task BackupDatabase_IsWalSafeStandaloneAndContainsCompleteFinancialData()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        await fixture.Context.Database.OpenConnectionAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint = 0;");
        await fixture.AddRepresentativeDataAsync("A");

        Assert.True(File.Exists(fixture.LivePath + "-wal"));
        var result = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(result.Success);

        await using var backup = fixture.CreateIndependentContext(result.BackupPath!);
        Assert.Equal("ok", await ScalarStringAsync(result.BackupPath!, "PRAGMA integrity_check;"));
        Assert.Equal(0L, await ScalarLongAsync(result.BackupPath!, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        Assert.Equal(3L, await ScalarLongAsync(result.BackupPath!, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        Assert.Equal(1, await backup.Clients.CountAsync(client => client.Name == "Client A"));
        Assert.Equal(125_075, await backup.Sales.Where(sale => sale.Reference == "SALE-A").Select(sale => sale.TotalAmountMinor).SingleAsync());
        Assert.Equal(80_000, await backup.Purchases.Where(purchase => purchase.Reference == "PURCHASE-A").Select(purchase => purchase.TotalAmountMinor).SingleAsync());
        Assert.Equal(1, await backup.CustomerPayments.CountAsync(payment => payment.Reference == "CP-A"));
        Assert.Equal(1, await backup.SupplierPayments.CountAsync(payment => payment.Reference == "SP-A"));
        Assert.Equal(1, await backup.Expenses.CountAsync(expense => expense.Reference == "EXP-A"));
        Assert.Equal(1, await backup.TreasuryOpeningBalances.CountAsync());
        Assert.False(File.Exists(result.BackupPath + "-wal"));
        Assert.False(File.Exists(result.BackupPath + "-shm"));
    }

    [Fact]
    public async Task Restore_ReplacesWholeDatabasePreservesSafetyBackupAndClearsSession()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        await fixture.AddRepresentativeDataAsync("A");
        var backupA = await fixture.Service.CreateLocalBackupAsync();
        var sourceHashBefore = await HashFileAsync(backupA.BackupPath!);

        fixture.Context.Clients.Add(new Client
        {
            Name = "Client B",
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.RestoreAsync(new(backupA.BackupPath!));

        Assert.True(result.Success);
        Assert.True(result.RestartRequired);
        Assert.Equal(backupA.BackupPath, result.RestoredFromPath);
        Assert.True(File.Exists(result.PreRestoreBackupPath));
        Assert.False(fixture.Session.IsAuthenticated);
        Assert.Equal(sourceHashBefore, await HashFileAsync(backupA.BackupPath!));

        await using var restored = fixture.CreateIndependentContext(fixture.LivePath);
        Assert.Equal(1, await restored.Clients.CountAsync(client => client.Name == "Client A"));
        Assert.Equal(0, await restored.Clients.CountAsync(client => client.Name == "Client B"));

        await using var safety = fixture.CreateIndependentContext(result.PreRestoreBackupPath!);
        Assert.Equal(1, await safety.Clients.CountAsync(client => client.Name == "Client B"));
        Assert.Equal("ok", await ScalarStringAsync(result.PreRestoreBackupPath!, "PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task Restore_DoesNotReplayStaleLiveWal()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("State A"));
        await fixture.Context.SaveChangesAsync();
        var backupA = await fixture.Service.CreateLocalBackupAsync();

        await fixture.Context.Database.OpenConnectionAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint = 0;");
        fixture.Context.Clients.Add(NewClient("State B in WAL"));
        await fixture.Context.SaveChangesAsync();
        Assert.True(File.Exists(fixture.LivePath + "-wal"));

        var result = await fixture.Service.RestoreAsync(new(backupA.BackupPath!));
        Assert.True(result.Success);

        await using var restored = fixture.CreateIndependentContext(fixture.LivePath);
        Assert.Equal(1, await restored.Clients.CountAsync(client => client.Name == "State A"));
        Assert.Equal(0, await restored.Clients.CountAsync(client => client.Name == "State B in WAL"));
    }

    [Fact]
    public async Task Restore_RollsBackAtomicallyWhenPostSwapValidationFails()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Candidate A"));
        await fixture.Context.SaveChangesAsync();
        var backupA = await fixture.Service.CreateLocalBackupAsync();

        await fixture.Context.Database.OpenConnectionAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint = 0;");
        var candidateA = await fixture.Context.Clients.SingleAsync(client => client.Name == "Candidate A");
        fixture.Context.Clients.Remove(candidateA);
        fixture.Context.Clients.Add(NewClient("Live B"));
        await fixture.Context.SaveChangesAsync();
        Assert.True(File.Exists(fixture.LivePath + "-wal"));

        var candidateWasObservedAfterSwap = false;
        fixture.RestoreTestHook.AfterAtomicReplace = livePath =>
        {
            candidateWasObservedAfterSwap =
                ScalarLongSynchronously(livePath, "SELECT COUNT(*) FROM Clients WHERE Name='Candidate A';") == 1 &&
                ScalarLongSynchronously(livePath, "SELECT COUNT(*) FROM Clients WHERE Name='Live B';") == 0;
            ExecuteSynchronously(livePath, "DROP INDEX UX_TreasuryOpeningBalance_Singleton;");
        };

        var result = await fixture.Service.RestoreAsync(new(backupA.BackupPath!));

        Assert.False(result.Success);
        Assert.True(candidateWasObservedAfterSwap);
        Assert.Equal(DatabaseBackupFailureReason.RestoreFailed, result.FailureReason);
        Assert.False(result.RestartRequired);
        Assert.True(File.Exists(result.PreRestoreBackupPath));
        Assert.True(fixture.Session.IsAuthenticated);
        Assert.False(File.Exists(fixture.LivePath + "-wal"));
        Assert.False(File.Exists(fixture.LivePath + "-shm"));

        await using var restoredLive = fixture.CreateIndependentContext(fixture.LivePath);
        Assert.Equal(0, await restoredLive.Clients.CountAsync(client => client.Name == "Candidate A"));
        Assert.Equal(1, await restoredLive.Clients.CountAsync(client => client.Name == "Live B"));
        Assert.Equal("ok", await ScalarStringAsync(fixture.LivePath, "PRAGMA integrity_check;"));
        Assert.Equal(1L, await ScalarLongAsync(fixture.LivePath,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='UX_TreasuryOpeningBalance_Singleton';"));

        await using var safety = fixture.CreateIndependentContext(result.PreRestoreBackupPath!);
        Assert.Equal(1, await safety.Clients.CountAsync(client => client.Name == "Live B"));
    }

    [Fact]
    public async Task Restore_RejectsInvalidForeignFutureAndInconsistentBackupsWithoutChangingLive()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();

        var missing = await fixture.Service.RestoreAsync(new(Path.Combine(fixture.Root, "missing.db")));
        Assert.Equal(DatabaseBackupFailureReason.BackupFileNotFound, missing.FailureReason);

        var textPath = Path.Combine(fixture.Root, "text.db");
        await File.WriteAllTextAsync(textPath, "not sqlite");
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, textPath, DatabaseBackupFailureReason.InvalidBackup);

        var foreignPath = Path.Combine(fixture.Root, "foreign.db");
        await ExecuteAsync(foreignPath, "CREATE TABLE Test(Id INTEGER PRIMARY KEY);");
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, foreignPath, DatabaseBackupFailureReason.InvalidBackup);

        var futurePath = Path.Combine(fixture.Root, "future.db");
        await CreateApplicationDatabaseAsync(futurePath, migrationCount: 3, activeResponsible: true);
        await ExecuteAsync(futurePath,
            "INSERT INTO __EFMigrationsHistory(MigrationId, ProductVersion) VALUES ('99999999999999_004_Future', '99.0.0');");
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, futurePath, DatabaseBackupFailureReason.IncompatibleBackup);

        var inconsistentPath = Path.Combine(fixture.Root, "inconsistent.db");
        await CreateApplicationDatabaseAsync(inconsistentPath, migrationCount: 3, activeResponsible: true);
        await ExecuteAsync(inconsistentPath,
            "DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_002_%';");
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, inconsistentPath, DatabaseBackupFailureReason.IncompatibleBackup);

        Assert.True(fixture.Session.IsAuthenticated);
        Assert.Empty(Directory.Exists(fixture.BackupDirectory)
            ? Directory.GetFiles(fixture.BackupDirectory, "*PreRestore*")
            : []);
    }

    [Fact]
    public async Task Restore_MigratesValidVersion002StagingWithoutChangingSource()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var oldBackup = Path.Combine(fixture.Root, "old-002.db");
        await CreateApplicationDatabaseAsync(oldBackup, migrationCount: 2, activeResponsible: true, clientName: "Old data");
        var hashBefore = await HashFileAsync(oldBackup);

        var result = await fixture.Service.RestoreAsync(new(oldBackup));
        Assert.True(result.Success);
        Assert.Equal(hashBefore, await HashFileAsync(oldBackup));
        Assert.Equal(2L, await ScalarLongAsync(oldBackup, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        Assert.Equal(3L, await ScalarLongAsync(fixture.LivePath, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        Assert.Equal(1L, await ScalarLongAsync(fixture.LivePath,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='UX_TreasuryOpeningBalance_Singleton';"));
        Assert.Equal(1L, await ScalarLongAsync(fixture.LivePath, "SELECT COUNT(*) FROM Clients WHERE Name='Old data';"));
    }

    [Fact]
    public async Task Restore_RejectsVersion002WhoseDataCannotSatisfyMigration003()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();
        var oldBackup = Path.Combine(fixture.Root, "old-duplicate-openings.db");
        await CreateApplicationDatabaseAsync(oldBackup, migrationCount: 2, activeResponsible: true);
        await ExecuteAsync(oldBackup,
            "INSERT INTO TreasuryOpeningBalance(OpeningDate,CashAmountMinor,BankAmountMinor,CreatedByUserId,CreatedAtUtc) " +
            "VALUES ('2026-01-01',0,0,1,'2026-01-01 00:00:00+00:00'),('2026-01-02',0,0,1,'2026-01-02 00:00:00+00:00');");

        var result = await fixture.Service.RestoreAsync(new(oldBackup));
        Assert.Equal(DatabaseBackupFailureReason.IncompatibleBackup, result.FailureReason);
        await fixture.AssertLiveMarkerAsync();
        Assert.Equal(2L, await ScalarLongAsync(oldBackup, "SELECT COUNT(*) FROM TreasuryOpeningBalance;"));
    }

    [Fact]
    public async Task Restore_RejectsTamperedCurrentSchemaAndDatabaseWithoutActiveResponsable()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();

        var tampered = Path.Combine(fixture.Root, "tampered.db");
        await CreateApplicationDatabaseAsync(tampered, migrationCount: 3, activeResponsible: true);
        await ExecuteAsync(tampered, "DROP INDEX UX_TreasuryOpeningBalance_Singleton;");
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, tampered, DatabaseBackupFailureReason.InvalidBackup);

        var lockedOut = Path.Combine(fixture.Root, "no-active-responsable.db");
        await CreateApplicationDatabaseAsync(lockedOut, migrationCount: 3, activeResponsible: false);
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, lockedOut, DatabaseBackupFailureReason.InvalidBackup);
        Assert.True(fixture.Session.IsAuthenticated);
    }

    [Fact]
    public async Task CancellationBeforeWork_LeavesLiveAndDestinationsUntouched()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();
        var source = await fixture.Service.CreateLocalBackupAsync();
        var filesBefore = Directory.GetFiles(fixture.BackupDirectory).Order().ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.CreateLocalBackupAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.RestoreAsync(new(source.BackupPath!), cancellation.Token));

        Assert.Equal(filesBefore, Directory.GetFiles(fixture.BackupDirectory).Order().ToArray());
        await fixture.AssertLiveMarkerAsync();
        Assert.True(fixture.Session.IsAuthenticated);
    }

    [Fact]
    public async Task Restore_RejectsTheLiveDatabaseAsItsOwnSource()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var result = await fixture.Service.RestoreAsync(new(fixture.LivePath.ToUpperInvariant()));
        Assert.Equal(DatabaseBackupFailureReason.InvalidBackup, result.FailureReason);
        Assert.True(fixture.Session.IsAuthenticated);
    }

    [Fact]
    public async Task InvalidBackupOutput_IsDeletedAfterValidationFailure()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        await ExecuteAsync(fixture.LivePath, "DROP INDEX UX_TreasuryOpeningBalance_Singleton;");

        var result = await fixture.Service.CreateLocalBackupAsync();

        Assert.False(result.Success);
        Assert.Equal(DatabaseBackupFailureReason.InvalidBackup, result.FailureReason);
        Assert.Empty(Directory.GetFiles(fixture.BackupDirectory));
    }

    [Fact]
    public async Task PreRestoreStorageFailure_LeavesLiveAndSessionUntouchedAndReleasesLock()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Candidate A"));
        await fixture.Context.SaveChangesAsync();
        var external = Directory.CreateDirectory(Path.Combine(fixture.Root, "restore-source")).FullName;
        var source = await fixture.Service.CreateExternalBackupAsync(new(external));
        fixture.Context.Clients.Add(NewClient("Live B"));
        await fixture.Context.SaveChangesAsync();
        await File.WriteAllTextAsync(fixture.BackupDirectory, "blocks directory creation");
        var swapWasInvoked = false;
        fixture.RestoreTestHook.AfterAtomicReplace = _ => swapWasInvoked = true;

        var result = await fixture.Service.RestoreAsync(new(source.BackupPath!));

        Assert.False(result.Success);
        Assert.False(swapWasInvoked);
        Assert.Equal(DatabaseBackupFailureReason.RestoreFailed, result.FailureReason);
        Assert.Null(result.PreRestoreBackupPath);
        Assert.True(fixture.Session.IsAuthenticated);
        await using (var live = fixture.CreateIndependentContext(fixture.LivePath))
        {
            Assert.Equal(1, await live.Clients.CountAsync(client => client.Name == "Live B"));
        }

        File.Delete(fixture.BackupDirectory);
        var operationAfterFailure = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(operationAfterFailure.Success);
    }

    private static Client NewClient(string name) => new()
    {
        Name = name,
        CreatedAtUtc = TestUtc,
        UpdatedAtUtc = TestUtc
    };

    private static async Task AssertRestoreRejectedAndLiveIntactAsync(
        BackupFixture fixture,
        string sourcePath,
        DatabaseBackupFailureReason expectedFailure)
    {
        var sourceHash = await HashFileAsync(sourcePath);
        var result = await fixture.Service.RestoreAsync(new(sourcePath));
        Assert.False(result.Success);
        Assert.Equal(expectedFailure, result.FailureReason);
        Assert.Equal(sourceHash, await HashFileAsync(sourcePath));
        await fixture.AssertLiveMarkerAsync();
    }

    private static async Task<byte[]> HashFileAsync(string path)
    {
        await using var stream = File.OpenRead(path);
        return await SHA256.HashDataAsync(stream);
    }

    private static async Task CreateApplicationDatabaseAsync(
        string path,
        int migrationCount,
        bool activeResponsible,
        string? clientName = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure($"Data Source={path};Pooling=False");
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        var migrations = context.Database.GetMigrations().ToArray();
        await context.GetService<IMigrator>().MigrateAsync(migrations[migrationCount - 1]);
        context.Users.Add(new User
        {
            Id = 1,
            FullName = "Backup owner",
            Username = "backup-owner",
            PasswordHash = "unused",
            Role = UserRole.Responsable,
            IsActive = activeResponsible,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        });
        if (clientName is not null)
        {
            context.Clients.Add(NewClient(clientName));
        }

        await context.SaveChangesAsync();
        await context.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();
    }

    private static async Task ExecuteAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                ForeignKeys = true
            }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static void ExecuteSynchronously(string databasePath, string sql)
    {
        using var connection = NewConnection(databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ScalarLongSynchronously(string databasePath, string sql)
    {
        using var connection = NewConnection(databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static async Task<long> ScalarLongAsync(string databasePath, string sql)
    {
        await using var connection = NewConnection(databasePath);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string?> ScalarStringAsync(string databasePath, string sql)
    {
        await using var connection = NewConnection(databasePath);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private static SqliteConnection NewConnection(string databasePath) => new(
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true
        }.ToString());

    private sealed class BackupFixture : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;

        private BackupFixture(string root, ServiceProvider provider, AsyncServiceScope scope)
        {
            Root = root;
            LivePath = Path.Combine(root, "live.db");
            BackupDirectory = Path.Combine(root, "backups");
            this.provider = provider;
            this.scope = scope;
            Context = scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
            Service = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
            Session = scope.ServiceProvider.GetRequiredService<ICurrentUserSession>();
            RestoreTestHook = scope.ServiceProvider.GetRequiredService<DatabaseRestoreTestHook>();
        }

        public string Root { get; }
        public string LivePath { get; }
        public string BackupDirectory { get; }
        public FinancialManagementDbContext Context { get; }
        public IDatabaseBackupService Service { get; }
        public ICurrentUserSession Session { get; }
        public DatabaseRestoreTestHook RestoreTestHook { get; }

        public static async Task<BackupFixture> CreateAsync(bool authenticate = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"FinancialManagementBackupTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var livePath = Path.Combine(root, "live.db");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestUtc));
            services.AddSingleton(new DatabaseBackupOptions(Path.Combine(root, "backups")));
            services.AddInfrastructure($"Data Source={livePath};Pooling=False");
            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var scope = provider.CreateAsyncScope();
            var fixture = new BackupFixture(root, provider, scope);
            await fixture.Context.Database.MigrateAsync();
            await fixture.AddUserAsync("responsable", UserRole.Responsable, active: true);
            if (authenticate)
            {
                await fixture.AuthenticateAsync("responsable", "Password-123!");
            }

            return fixture;
        }

        public async Task AuthenticateAsync(string username, string password)
        {
            var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var result = await authentication.AuthenticateAsync(username, password);
            Assert.True(result.Success);
        }

        public async Task AddEmployeeWithEveryPermissionAsync()
        {
            var employee = await AddUserAsync("employee", UserRole.Employe, active: true);
            Context.UserPermissions.AddRange(Enum.GetValues<PermissionModule>().Select(module => new UserPermission
            {
                UserId = employee.Id,
                Module = module,
                CanView = true,
                CanCreate = true,
                CanModify = true
            }));
            await Context.SaveChangesAsync();
        }

        public async Task AddRepresentativeDataAsync(string suffix)
        {
            var userId = await Context.Users
                .Where(user => user.Username == "responsable")
                .Select(user => user.Id)
                .SingleAsync();
            var client = new Client
            {
                Name = $"Client {suffix}",
                Phone = "0600000000",
                CreatedAtUtc = TestUtc,
                UpdatedAtUtc = TestUtc
            };
            var supplier = new Supplier
            {
                Name = $"Supplier {suffix}",
                CreatedAtUtc = TestUtc,
                UpdatedAtUtc = TestUtc
            };
            Context.AddRange(client, supplier);
            await Context.SaveChangesAsync();

            Context.AddRange(
                new Sale
                {
                    Reference = $"SALE-{suffix}",
                    SaleDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    TotalAmountMinor = 125_075,
                    ClientId = client.Id,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc,
                    UpdatedAtUtc = TestUtc
                },
                new CustomerPayment
                {
                    Reference = $"CP-{suffix}",
                    ClientId = client.Id,
                    PaymentDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    AmountMinor = 25_000,
                    PaymentMethod = PaymentMethod.Cash,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc
                },
                new Purchase
                {
                    Reference = $"PURCHASE-{suffix}",
                    PurchaseDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    TotalAmountMinor = 80_000,
                    SupplierId = supplier.Id,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc,
                    UpdatedAtUtc = TestUtc
                },
                new SupplierPayment
                {
                    Reference = $"SP-{suffix}",
                    SupplierId = supplier.Id,
                    PaymentDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    AmountMinor = 15_000,
                    PaymentMethod = PaymentMethod.BankTransfer,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc
                },
                new Expense
                {
                    Reference = $"EXP-{suffix}",
                    ExpenseDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    ExpenseCategoryId = 1,
                    AmountMinor = 5_000,
                    PaymentMethod = PaymentMethod.Cash,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc,
                    UpdatedAtUtc = TestUtc
                },
                new TreasuryOpeningBalance
                {
                    OpeningDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    CashAmountMinor = 100_000,
                    BankAmountMinor = 200_000,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc
                });
            await Context.SaveChangesAsync();
        }

        public FinancialManagementDbContext CreateIndependentContext(string databasePath)
        {
            var options = new DbContextOptionsBuilder<FinancialManagementDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            return new FinancialManagementDbContext(options);
        }

        public async Task AssertLiveMarkerAsync()
        {
            await using var verification = CreateIndependentContext(LivePath);
            Assert.Equal(1, await verification.Clients.CountAsync(client => client.Name == "Live marker"));
        }

        private async Task<User> AddUserAsync(string username, UserRole role, bool active)
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var user = new User
            {
                FullName = username,
                Username = username,
                PasswordHash = hasher.HashPassword("Password-123!"),
                Role = role,
                IsActive = active,
                CreatedAtUtc = TestUtc,
                UpdatedAtUtc = TestUtc
            };
            Context.Users.Add(user);
            await Context.SaveChangesAsync();
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
            SqliteConnection.ClearAllPools();
            try
            {
                Directory.Delete(Root, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
