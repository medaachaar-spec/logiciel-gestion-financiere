namespace FinancialManagement.Application.Backup;

public sealed record CreateDatabaseBackupResult(
    bool Success,
    DatabaseBackupFailureReason FailureReason,
    string? BackupPath,
    string? FileName,
    DateTimeOffset? CreatedAtUtc,
    long? FileSizeBytes)
{
    public static CreateDatabaseBackupResult Failed(DatabaseBackupFailureReason failureReason) =>
        new(false, failureReason, null, null, null, null);

    public static CreateDatabaseBackupResult Succeeded(
        string backupPath,
        DateTimeOffset createdAtUtc,
        long fileSizeBytes) =>
        new(true, DatabaseBackupFailureReason.None, backupPath, Path.GetFileName(backupPath), createdAtUtc, fileSizeBytes);
}
