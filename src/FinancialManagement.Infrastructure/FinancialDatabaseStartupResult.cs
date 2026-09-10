using FinancialManagement.Application.Backup;

namespace FinancialManagement.Infrastructure;

public sealed record FinancialDatabaseStartupResult(
    string? PreMigrationBackupPath,
    StartupDatabaseBackupResult? DailyBackup);

public sealed class PreMigrationBackupException : Exception
{
    public PreMigrationBackupException(DatabaseBackupFailureReason failureReason)
        : base("A required pre-migration backup could not be created.")
    {
        FailureReason = failureReason;
    }

    public DatabaseBackupFailureReason FailureReason { get; }
}

public sealed class DatabaseMigrationWithBackupException : Exception
{
    public DatabaseMigrationWithBackupException(string recoveryBackupPath, Exception innerException)
        : base("Database migration failed after a recovery backup was created.", innerException)
    {
        RecoveryBackupPath = recoveryBackupPath;
    }

    public string RecoveryBackupPath { get; }
}

internal sealed class DatabaseStartupTestHook
{
    public Action? BeforeMigration { get; set; }

    public void InvokeBeforeMigration() => BeforeMigration?.Invoke();
}
