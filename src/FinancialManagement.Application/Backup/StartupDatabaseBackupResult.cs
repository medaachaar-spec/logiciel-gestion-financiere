namespace FinancialManagement.Application.Backup;

public sealed record StartupDatabaseBackupResult(
    bool Success,
    bool Created,
    DatabaseBackupFailureReason FailureReason,
    string? BackupPath,
    DateOnly LocalBusinessDate)
{
    public static StartupDatabaseBackupResult Failed(
        DatabaseBackupFailureReason failureReason,
        DateOnly localBusinessDate) =>
        new(false, false, failureReason, null, localBusinessDate);

    public static StartupDatabaseBackupResult AlreadyExists(
        string backupPath,
        DateOnly localBusinessDate) =>
        new(true, false, DatabaseBackupFailureReason.None, backupPath, localBusinessDate);

    public static StartupDatabaseBackupResult Succeeded(
        string backupPath,
        DateOnly localBusinessDate) =>
        new(true, true, DatabaseBackupFailureReason.None, backupPath, localBusinessDate);
}
