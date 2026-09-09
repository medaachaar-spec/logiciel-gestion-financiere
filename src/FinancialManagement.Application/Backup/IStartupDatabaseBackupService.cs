namespace FinancialManagement.Application.Backup;

public interface IStartupDatabaseBackupService
{
    Task<StartupDatabaseBackupResult> CreatePreMigrationBackupAsync(
        CancellationToken cancellationToken = default);

    Task<StartupDatabaseBackupResult> CreateDailyBackupIfNeededAsync(
        CancellationToken cancellationToken = default);
}
