namespace FinancialManagement.Application.Backup;

public interface IDatabaseBackupService
{
    Task<CreateDatabaseBackupResult> CreateLocalBackupAsync(
        CancellationToken cancellationToken = default);

    Task<CreateDatabaseBackupResult> CreateExternalBackupAsync(
        CreateExternalDatabaseBackupCommand command,
        CancellationToken cancellationToken = default);

    Task<RestoreDatabaseBackupResult> RestoreAsync(
        RestoreDatabaseBackupCommand command,
        CancellationToken cancellationToken = default);
}
