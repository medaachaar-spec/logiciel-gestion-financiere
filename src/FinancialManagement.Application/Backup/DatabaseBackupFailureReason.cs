namespace FinancialManagement.Application.Backup;

public enum DatabaseBackupFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    DestinationDirectoryNotFound,
    BackupFileNotFound,
    InvalidBackup,
    IncompatibleBackup,
    StorageFailure,
    RestoreFailed
}
