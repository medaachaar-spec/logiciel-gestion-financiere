namespace FinancialManagement.Application.Backup;

public sealed record RestoreDatabaseBackupResult(
    bool Success,
    DatabaseBackupFailureReason FailureReason,
    string? RestoredFromPath,
    string? PreRestoreBackupPath,
    bool RestartRequired)
{
    public static RestoreDatabaseBackupResult Failed(
        DatabaseBackupFailureReason failureReason,
        string? restoredFromPath = null,
        string? preRestoreBackupPath = null) =>
        new(false, failureReason, restoredFromPath, preRestoreBackupPath, false);

    public static RestoreDatabaseBackupResult Succeeded(
        string restoredFromPath,
        string preRestoreBackupPath) =>
        new(true, DatabaseBackupFailureReason.None, restoredFromPath, preRestoreBackupPath, true);
}
