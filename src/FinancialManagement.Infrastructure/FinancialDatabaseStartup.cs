using FinancialManagement.Application.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure;

/// <summary>Startup boundary: validate legacy storage, migrate, validate current storage.</summary>
public static class FinancialDatabaseStartup
{
    public static Task<FinancialDatabaseStartupResult> InitializeAsync(
        FinancialManagementDbContext context,
        IStartupDatabaseBackupService backupService,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(backupService);
        return InitializeCoreAsync(context, backupService, cancellationToken, testHook: null);
    }

    internal static Task<FinancialDatabaseStartupResult> InitializeForTestsAsync(
        FinancialManagementDbContext context,
        IStartupDatabaseBackupService backupService,
        DatabaseStartupTestHook testHook,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(backupService);
        ArgumentNullException.ThrowIfNull(testHook);
        return InitializeCoreAsync(context, backupService, cancellationToken, testHook);
    }

    private static async Task<FinancialDatabaseStartupResult> InitializeCoreAsync(
        FinancialManagementDbContext context,
        IStartupDatabaseBackupService backupService,
        CancellationToken cancellationToken,
        DatabaseStartupTestHook? testHook)
    {
        string? preMigrationBackupPath = null;
        StartupDatabaseBackupResult? dailyBackup = null;
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            if (!await FinancialStorageValidator.ValidateAsync(connection, allowEmpty: true, cancellationToken))
                throw new FinancialStorageIntegrityException();

            var appliedMigrations = (await context.Database
                .GetAppliedMigrationsAsync(cancellationToken)).ToArray();
            var pendingMigrations = (await context.Database
                .GetPendingMigrationsAsync(cancellationToken)).ToArray();
            if (appliedMigrations.Length > 0
                && pendingMigrations.Length > 0)
            {
                var preMigrationBackup = await backupService
                    .CreatePreMigrationBackupAsync(cancellationToken);
                if (!preMigrationBackup.Success
                    || !preMigrationBackup.Created
                    || string.IsNullOrWhiteSpace(preMigrationBackup.BackupPath)
                    || !File.Exists(preMigrationBackup.BackupPath)
                    || new FileInfo(preMigrationBackup.BackupPath).Length == 0)
                {
                    throw new PreMigrationBackupException(
                        preMigrationBackup.FailureReason == DatabaseBackupFailureReason.None
                            ? DatabaseBackupFailureReason.StorageFailure
                            : preMigrationBackup.FailureReason);
                }

                preMigrationBackupPath = Path.GetFullPath(preMigrationBackup.BackupPath);
            }

            try
            {
                testHook?.InvokeBeforeMigration();
                await context.Database.MigrateAsync(cancellationToken);
            }
            catch (Exception exception) when (
                preMigrationBackupPath is not null
                && exception is not OperationCanceledException)
            {
                throw new DatabaseMigrationWithBackupException(
                    preMigrationBackupPath,
                    exception);
            }

            if (!await FinancialStorageValidator.ValidateAsync(connection, allowEmpty: false, cancellationToken))
            {
                var validationException = new FinancialStorageIntegrityException();
                if (preMigrationBackupPath is not null)
                {
                    throw new DatabaseMigrationWithBackupException(
                        preMigrationBackupPath,
                        validationException);
                }

                throw validationException;
            }

            dailyBackup = await backupService
                .CreateDailyBackupIfNeededAsync(cancellationToken);

            return new FinancialDatabaseStartupResult(
                preMigrationBackupPath,
                dailyBackup);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}

public sealed class FinancialStorageIntegrityException : Exception
{
    public FinancialStorageIntegrityException()
        : base("Financial storage integrity validation failed.") { }
}
