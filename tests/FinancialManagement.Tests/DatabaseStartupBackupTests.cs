using System.IO.Compression;
using System.Security.Cryptography;
using FinancialManagement.Application.Backup;
using FinancialManagement.Infrastructure;
using FinancialManagement.Infrastructure.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class DatabaseStartupBackupTests
{
    [Fact]
    public async Task Current_database_has_no_pending_migration_and_creates_no_pre_migration_backup()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();

        var result = await FinancialDatabaseStartup.InitializeAsync(
            fixture.Context,
            fixture.BackupService);

        Assert.Null(result.PreMigrationBackupPath);
        Assert.Empty(fixture.PreMigrationBackups());
        Assert.True(result.DailyBackup?.Success);
    }

    [Fact]
    public async Task Fresh_database_migrates_without_pre_migration_backup()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();

        var result = await FinancialDatabaseStartup.InitializeAsync(
            fixture.Context,
            fixture.BackupService);

        Assert.Null(result.PreMigrationBackupPath);
        Assert.Empty(fixture.PreMigrationBackups());
        Assert.True(result.DailyBackup?.Created);
        Assert.Equal(fixture.Migrations, await fixture.Context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Pending_migration_creates_old_schema_backup_before_migration_then_daily_backup()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.MigrateToVersion003Async();
        var backupWasPresentBeforeMigration = false;
        var hook = new DatabaseStartupTestHook
        {
            BeforeMigration = () =>
                backupWasPresentBeforeMigration = fixture.PreMigrationBackups().Length == 1
        };

        var result = await FinancialDatabaseStartup.InitializeForTestsAsync(
            fixture.Context,
            fixture.BackupService,
            hook);

        Assert.True(backupWasPresentBeforeMigration);
        Assert.NotNull(result.PreMigrationBackupPath);
        Assert.True(File.Exists(result.PreMigrationBackupPath));
        Assert.Single(fixture.PreMigrationBackups());
        Assert.Single(fixture.DailyBackups());
        Assert.True(result.DailyBackup?.Created);
        Assert.Equal(fixture.Migrations, await fixture.Context.Database.GetAppliedMigrationsAsync());

        var extractedPath = await fixture.ExtractBackupAsync(result.PreMigrationBackupPath!);
        await using var oldContext = fixture.CreateContext(extractedPath);
        Assert.Equal(fixture.Migrations.Take(3), await oldContext.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task Pre_migration_backup_failure_aborts_without_applying_pending_migration()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.MigrateToVersion003Async();
        var service = new StubStartupBackupService(
            StartupDatabaseBackupResult.Failed(
                DatabaseBackupFailureReason.StorageFailure,
                new DateOnly(2026, 9, 9)));

        var exception = await Assert.ThrowsAsync<PreMigrationBackupException>(() =>
            FinancialDatabaseStartup.InitializeAsync(fixture.Context, service));

        Assert.Equal(DatabaseBackupFailureReason.StorageFailure, exception.FailureReason);
        Assert.Equal(fixture.Migrations.Take(3), await fixture.Context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(1, service.PreMigrationCalls);
        Assert.Equal(0, service.DailyCalls);
    }

    [Fact]
    public async Task Missing_startup_backup_dependency_cannot_migrate_existing_database()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.MigrateToVersion003Async();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            FinancialDatabaseStartup.InitializeAsync(fixture.Context, null!));

        Assert.Equal(
            fixture.Migrations.Take(3),
            await fixture.Context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(fixture.PreMigrationBackups());
        Assert.Empty(fixture.DailyBackups());
    }

    [Fact]
    public async Task Migration_failure_keeps_pre_migration_recovery_artifact_and_fails_startup()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.MigrateToVersion003Async();
        var hook = new DatabaseStartupTestHook
        {
            BeforeMigration = () => throw new InvalidOperationException("Deliberate migration failure")
        };

        var exception = await Assert.ThrowsAsync<DatabaseMigrationWithBackupException>(() =>
            FinancialDatabaseStartup.InitializeForTestsAsync(
                fixture.Context,
                fixture.BackupService,
                hook));

        Assert.True(File.Exists(exception.RecoveryBackupPath));
        Assert.Contains("PreMigration", Path.GetFileName(exception.RecoveryBackupPath), StringComparison.Ordinal);
        Assert.Equal(fixture.Migrations.Take(3), await fixture.Context.Database.GetAppliedMigrationsAsync());
        Assert.Empty(fixture.DailyBackups());
    }

    [Fact]
    public async Task Daily_backup_is_created_once_per_local_date_and_pre_migration_does_not_suppress_it()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.MigrateToVersion003Async();

        var first = await FinancialDatabaseStartup.InitializeAsync(fixture.Context, fixture.BackupService);
        var second = await FinancialDatabaseStartup.InitializeAsync(fixture.Context, fixture.BackupService);

        Assert.NotNull(first.PreMigrationBackupPath);
        Assert.True(first.DailyBackup?.Created);
        Assert.False(second.DailyBackup?.Created);
        Assert.Single(fixture.PreMigrationBackups());
        Assert.Single(fixture.DailyBackups());

        fixture.Clock.UtcNow = fixture.Clock.UtcNow.AddDays(1);
        var nextDay = await FinancialDatabaseStartup.InitializeAsync(fixture.Context, fixture.BackupService);

        Assert.True(nextDay.DailyBackup?.Created);
        Assert.Equal(2, fixture.DailyBackups().Length);
    }

    [Fact]
    public async Task Concurrent_daily_backup_requests_publish_exactly_one_artifact()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();

        var results = await Task.WhenAll(
            fixture.BackupService.CreateDailyBackupIfNeededAsync(),
            fixture.BackupService.CreateDailyBackupIfNeededAsync(),
            fixture.BackupService.CreateDailyBackupIfNeededAsync());

        Assert.All(results, result => Assert.True(result.Success));
        Assert.Single(results, result => result.Created);
        Assert.Single(fixture.DailyBackups());
        Assert.All(results, result => Assert.StartsWith(
            Path.GetFullPath(fixture.BackupDirectory),
            Path.GetFullPath(result.BackupPath!),
            StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Valid_daily_artifact_satisfies_the_date_without_duplication()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();

        var created = await fixture.BackupService.CreateDailyBackupIfNeededAsync();
        var existing = await fixture.BackupService.CreateDailyBackupIfNeededAsync();

        Assert.True(created.Created);
        Assert.True(existing.Success);
        Assert.False(existing.Created);
        Assert.Equal(created.BackupPath, existing.BackupPath);
        Assert.Single(fixture.DailyBackups());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Zero_byte_or_malformed_daily_artifact_is_ignored_and_replaced(bool zeroByte)
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        Directory.CreateDirectory(fixture.BackupDirectory);
        var invalidPath = fixture.DailyPath(zeroByte ? "zero" : "malformed");
        if (zeroByte)
        {
            await File.WriteAllBytesAsync(invalidPath, []);
        }
        else
        {
            await File.WriteAllTextAsync(invalidPath, "not a backup artifact");
        }

        var result = await fixture.BackupService.CreateDailyBackupIfNeededAsync();

        Assert.True(result.Created);
        Assert.NotEqual(Path.GetFullPath(invalidPath), result.BackupPath);
        Assert.True(File.Exists(invalidPath));
        Assert.Equal(2, fixture.DailyBackups().Length);
        await fixture.AssertCurrentArtifactAsync(result.BackupPath!);
    }

    [Fact]
    public async Task Previously_valid_daily_artifact_corrupted_later_is_replaced()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var original = await fixture.BackupService.CreateDailyBackupIfNeededAsync();
        await File.WriteAllTextAsync(original.BackupPath!, "corrupted after publication");

        var replacement = await fixture.BackupService.CreateDailyBackupIfNeededAsync();

        Assert.True(replacement.Created);
        Assert.NotEqual(original.BackupPath, replacement.BackupPath);
        Assert.True(File.Exists(original.BackupPath));
        Assert.Equal(2, fixture.DailyBackups().Length);
        await fixture.AssertCurrentArtifactAsync(replacement.BackupPath!);
    }

    [Fact]
    public async Task Invalid_and_valid_daily_artifacts_coexist_without_duplication()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var valid = await fixture.BackupService.CreateDailyBackupIfNeededAsync();
        var invalidPath = fixture.DailyPath("zzzz-invalid");
        await File.WriteAllTextAsync(invalidPath, "not a backup artifact");

        var result = await fixture.BackupService.CreateDailyBackupIfNeededAsync();

        Assert.True(result.Success);
        Assert.False(result.Created);
        Assert.Equal(valid.BackupPath, result.BackupPath);
        Assert.Equal(2, fixture.DailyBackups().Length);
    }

    [Fact]
    public async Task Corrupted_daily_and_failed_replacement_remain_eligible_for_retry()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var original = await fixture.BackupService.CreateDailyBackupIfNeededAsync();
        await File.WriteAllTextAsync(original.BackupPath!, "corrupted after publication");
        await fixture.Context.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();
        var liveHash = await fixture.LiveDatabaseHashAsync();

        StartupDatabaseBackupResult failed;
        using (new FileStream(
                   fixture.LiveDatabasePath,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.None))
        {
            failed = await fixture.BackupService.CreateDailyBackupIfNeededAsync();
        }

        Assert.False(failed.Success);
        Assert.False(failed.Created);
        Assert.Equal(DatabaseBackupFailureReason.StorageFailure, failed.FailureReason);
        Assert.Single(fixture.DailyBackups());
        Assert.Equal(liveHash, await fixture.LiveDatabaseHashAsync());

        var retry = await fixture.BackupService.CreateDailyBackupIfNeededAsync();
        Assert.True(retry.Created);
        Assert.Equal(2, fixture.DailyBackups().Length);
        Assert.Equal(liveHash, await fixture.LiveDatabaseHashAsync());
        await fixture.AssertCurrentArtifactAsync(retry.BackupPath!);
    }

    [Fact]
    public async Task Daily_recheck_after_guard_acquisition_observes_newly_published_valid_artifact()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        Directory.CreateDirectory(fixture.BackupDirectory);
        var guardPath = Path.Combine(fixture.BackupDirectory, ".daily-backup-20260909.guard");
        string publishedPath;

        using (new FileStream(
                   guardPath,
                   FileMode.OpenOrCreate,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            var artifact = await fixture.BackupService.CreatePreMigrationBackupAsync();
            publishedPath = fixture.DailyPath("published-by-first-process");
            File.Move(artifact.BackupPath!, publishedPath);
        }

        var secondProcessResult = await fixture.BackupService.CreateDailyBackupIfNeededAsync();

        Assert.True(secondProcessResult.Success);
        Assert.False(secondProcessResult.Created);
        Assert.Equal(Path.GetFullPath(publishedPath), secondProcessResult.BackupPath);
        Assert.Single(fixture.DailyBackups());
    }

    [Fact]
    public async Task Daily_backup_guard_prevents_a_second_process_from_publishing_concurrently()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        Directory.CreateDirectory(fixture.BackupDirectory);
        var guardPath = Path.Combine(fixture.BackupDirectory, ".daily-backup-20260909.guard");

        StartupDatabaseBackupResult blocked;
        using (var competingProcessGuard = new FileStream(
                   guardPath,
                   FileMode.OpenOrCreate,
                   FileAccess.ReadWrite,
                   FileShare.None))
        {
            blocked = await fixture.BackupService.CreateDailyBackupIfNeededAsync();
        }

        Assert.False(blocked.Success);
        Assert.Equal(DatabaseBackupFailureReason.StorageFailure, blocked.FailureReason);
        Assert.Empty(fixture.DailyBackups());

        var afterRelease = await fixture.BackupService.CreateDailyBackupIfNeededAsync();
        Assert.True(afterRelease.Created);
        Assert.Single(fixture.DailyBackups());
    }

    [Fact]
    public async Task Daily_backup_failure_is_reported_after_successful_validation_without_failing_startup()
    {
        await using var fixture = await StartupBackupFixture.CreateAsync();
        await fixture.Context.Database.MigrateAsync();
        var service = new StubStartupBackupService(
            preMigrationResult: null,
            dailyResult: StartupDatabaseBackupResult.Failed(
                DatabaseBackupFailureReason.StorageFailure,
                new DateOnly(2026, 9, 9)));

        var result = await FinancialDatabaseStartup.InitializeAsync(fixture.Context, service);

        Assert.False(result.DailyBackup?.Success);
        Assert.Equal(fixture.Migrations, await fixture.Context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(0, service.PreMigrationCalls);
        Assert.Equal(1, service.DailyCalls);
    }

    private sealed class StubStartupBackupService(
        StartupDatabaseBackupResult? preMigrationResult,
        StartupDatabaseBackupResult? dailyResult = null) : IStartupDatabaseBackupService
    {
        public int PreMigrationCalls { get; private set; }
        public int DailyCalls { get; private set; }

        public Task<StartupDatabaseBackupResult> CreatePreMigrationBackupAsync(
            CancellationToken cancellationToken = default)
        {
            PreMigrationCalls++;
            return Task.FromResult(preMigrationResult
                ?? throw new InvalidOperationException("No pre-migration result configured."));
        }

        public Task<StartupDatabaseBackupResult> CreateDailyBackupIfNeededAsync(
            CancellationToken cancellationToken = default)
        {
            DailyCalls++;
            return Task.FromResult(dailyResult
                ?? StartupDatabaseBackupResult.Failed(
                    DatabaseBackupFailureReason.StorageFailure,
                    new DateOnly(2026, 9, 9)));
        }
    }

    private sealed class StartupBackupFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private StartupBackupFixture(
            string root,
            MutableTimeProvider clock,
            ServiceProvider provider,
            AsyncServiceScope scope)
        {
            Root = root;
            BackupDirectory = Path.Combine(root, "backups");
            Clock = clock;
            _provider = provider;
            _scope = scope;
            Context = scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
            BackupService = scope.ServiceProvider.GetRequiredService<IStartupDatabaseBackupService>();
        }

        public string Root { get; }
        public string BackupDirectory { get; }
        public string LiveDatabasePath => Context.Database.GetDbConnection().DataSource;
        public MutableTimeProvider Clock { get; }
        public FinancialManagementDbContext Context { get; }
        public IStartupDatabaseBackupService BackupService { get; }
        public string[] Migrations => Context.Database.GetMigrations().ToArray();

        public static Task<StartupBackupFixture> CreateAsync()
        {
            var root = Path.Combine(
                Path.GetTempPath(),
                $"FinancialManagement-StartupBackup-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var clock = new MutableTimeProvider(
                new DateTimeOffset(2026, 9, 9, 12, 0, 0, TimeSpan.Zero));
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(clock);
            services.AddSingleton(new DatabaseBackupOptions(Path.Combine(root, "backups")));
            services.AddInfrastructure(
                $"Data Source={Path.Combine(root, "live.db")};Pooling=False");
            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var scope = provider.CreateAsyncScope();
            return Task.FromResult(new StartupBackupFixture(root, clock, provider, scope));
        }

        public async Task MigrateToVersion003Async() =>
            await Context.GetService<IMigrator>().MigrateAsync(Migrations[2]);

        public string[] PreMigrationBackups() =>
            Directory.Exists(BackupDirectory)
                ? Directory.GetFiles(BackupDirectory, "FinancialManagement_PreMigration_*.fmbak")
                : [];

        public string[] DailyBackups() =>
            Directory.Exists(BackupDirectory)
                ? Directory.GetFiles(BackupDirectory, "FinancialManagement_Daily_*.fmbak")
                : [];

        public string DailyPath(string suffix) =>
            Path.Combine(
                BackupDirectory,
                $"FinancialManagement_Daily_20260909_{suffix}{DatabaseBackupArtifact.Extension}");

        public async Task AssertCurrentArtifactAsync(string artifactPath)
        {
            var extractedPath = await ExtractBackupAsync(artifactPath);
            await using var artifactContext = CreateContext(extractedPath);
            Assert.Equal(Migrations, await artifactContext.Database.GetAppliedMigrationsAsync());
        }

        public async Task<string> LiveDatabaseHashAsync()
        {
            await using var stream = new FileStream(
                LiveDatabasePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read);
            return Convert.ToHexString(await SHA256.HashDataAsync(
                stream,
                CancellationToken.None));
        }

        public async Task<string> ExtractBackupAsync(string artifactPath)
        {
            var directory = Directory.CreateDirectory(
                Path.Combine(Root, "inspection-" + Guid.NewGuid().ToString("N"))).FullName;
            var databasePath = Path.Combine(directory, "database.sqlite");
            var failure = await DatabaseBackupArtifact.ExtractAsync(
                artifactPath,
                databasePath,
                CancellationToken.None);
            Assert.Equal(DatabaseBackupFailureReason.None, failure);
            return databasePath;
        }

        public FinancialManagementDbContext CreateContext(string databasePath) =>
            new(new DbContextOptionsBuilder<FinancialManagementDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options);

        public async ValueTask DisposeAsync()
        {
            await _scope.DisposeAsync();
            await _provider.DisposeAsync();
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
}
