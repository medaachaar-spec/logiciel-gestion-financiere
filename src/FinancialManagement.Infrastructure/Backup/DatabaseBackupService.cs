using System.Security.Cryptography;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Backup;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure.Authentication;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialManagement.Infrastructure.Backup;

internal sealed class DatabaseBackupService(
    FinancialManagementDbContext dbContext,
    CurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    DatabaseBackupOptions options,
    DatabaseMaintenanceLock maintenanceLock,
    DatabaseRestoreTestHook restoreTestHook,
    TimeProvider timeProvider,
    ILogger<DatabaseBackupService>? logger = null) : IDatabaseBackupService, IStartupDatabaseBackupService
{
    private const string MigrationHistoryTable = "__EFMigrationsHistory";

    public Task<CreateDatabaseBackupResult> CreateLocalBackupAsync(
        CancellationToken cancellationToken = default) =>
        CreateBackupAsync(options.LocalBackupDirectory, createDirectory: true, "Backup", cancellationToken);

    public Task<StartupDatabaseBackupResult> CreatePreMigrationBackupAsync(
        CancellationToken cancellationToken = default) =>
        CreateStartupBackupAsync("PreMigration", requireCurrentSchema: false, cancellationToken);

    public async Task<StartupDatabaseBackupResult> CreateDailyBackupIfNeededAsync(
        CancellationToken cancellationToken = default)
    {
        var localBusinessDate = GetLocalBusinessDate();
        await maintenanceLock.Semaphore.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(options.LocalBackupDirectory);
            var guardPath = Path.Combine(
                options.LocalBackupDirectory,
                $".daily-backup-{localBusinessDate:yyyyMMdd}.guard");
            using var dailyGuard = new FileStream(
                guardPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.WriteThrough);
            var pattern = $"FinancialManagement_Daily_{localBusinessDate:yyyyMMdd}_*{DatabaseBackupArtifact.Extension}";
            var knownMigrations = dbContext.Database.GetMigrations().ToArray();
            foreach (var candidatePath in Directory.EnumerateFiles(
                         options.LocalBackupDirectory,
                         pattern,
                         SearchOption.TopDirectoryOnly)
                     .OrderByDescending(path => path, StringComparer.OrdinalIgnoreCase))
            {
                if (await IsValidCurrentDailyArtifactAsync(
                        candidatePath,
                        knownMigrations,
                        cancellationToken))
                {
                    return StartupDatabaseBackupResult.AlreadyExists(
                        Path.GetFullPath(candidatePath),
                        localBusinessDate);
                }
            }

            var result = await CreateArtifactBackupAsync(
                options.LocalBackupDirectory,
                $"Daily_{localBusinessDate:yyyyMMdd}",
                requireActiveResponsible: false,
                cancellationToken);
            return ToStartupResult(result, localBusinessDate);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SqliteException
            or InvalidOperationException)
        {
            logger?.LogWarning(exception, "The automatic daily database backup could not be created.");
            return StartupDatabaseBackupResult.Failed(
                DatabaseBackupFailureReason.StorageFailure,
                localBusinessDate);
        }
        finally
        {
            maintenanceLock.Semaphore.Release();
        }
    }

    private async Task<bool> IsValidCurrentDailyArtifactAsync(
        string artifactPath,
        IReadOnlyList<string> knownMigrations,
        CancellationToken cancellationToken)
    {
        string? stagingPath = null;
        try
        {
            stagingPath = CreateUnusedPath(options.LocalBackupDirectory, "DailyValidation");
            var artifactFailure = await DatabaseBackupArtifact.ExtractAsync(
                artifactPath,
                stagingPath,
                cancellationToken);
            if (artifactFailure is not DatabaseBackupFailureReason.None)
            {
                logger?.LogWarning(
                    "Daily backup artifact {BackupPath} failed artifact validation and will not satisfy today's backup requirement.",
                    artifactPath);
                return false;
            }

            var databaseValidation = await ValidateDatabaseAsync(
                stagingPath,
                knownMigrations,
                requireCurrentSchema: true,
                requireActiveResponsible: false,
                cancellationToken);
            if (!databaseValidation.IsValid)
            {
                logger?.LogWarning(
                    "Daily backup artifact {BackupPath} failed database validation and will not satisfy today's backup requirement.",
                    artifactPath);
            }

            return databaseValidation.IsValid;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SqliteException
            or InvalidOperationException)
        {
            logger?.LogWarning(
                exception,
                "Daily backup artifact {BackupPath} could not be validated and will not satisfy today's backup requirement.",
                artifactPath);
            return false;
        }
        finally
        {
            DeleteDatabaseFilesBestEffort(stagingPath);
        }
    }

    public async Task<CreateDatabaseBackupResult> CreateExternalBackupAsync(
        CreateExternalDatabaseBackupCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var authorizationFailure = await GetAuthorizationFailureAsync(cancellationToken);
        if (authorizationFailure is not DatabaseBackupFailureReason.None)
        {
            return CreateDatabaseBackupResult.Failed(authorizationFailure);
        }

        if (string.IsNullOrWhiteSpace(command.DestinationDirectory) ||
            !Directory.Exists(command.DestinationDirectory))
        {
            return CreateDatabaseBackupResult.Failed(
                DatabaseBackupFailureReason.DestinationDirectoryNotFound);
        }

        return await CreateBackupAsync(
            Path.GetFullPath(command.DestinationDirectory),
            createDirectory: false,
            "Backup",
            cancellationToken);
    }

    public async Task<RestoreDatabaseBackupResult> RestoreAsync(
        RestoreDatabaseBackupCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var initiatingUserId = currentUserSession.UserId;
        var authorizationFailure = await ValidateMaintenanceAuthorizationAsync(
            initiatingUserId,
            cancellationToken);
        if (authorizationFailure is not DatabaseBackupFailureReason.None)
        {
            return RestoreDatabaseBackupResult.Failed(authorizationFailure);
        }

        if (string.IsNullOrWhiteSpace(command.BackupFilePath) ||
            !File.Exists(command.BackupFilePath))
        {
            return RestoreDatabaseBackupResult.Failed(DatabaseBackupFailureReason.BackupFileNotFound);
        }

        string sourcePath;
        string livePath;
        try
        {
            sourcePath = Path.GetFullPath(command.BackupFilePath);
            livePath = GetLiveDatabasePath();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return RestoreDatabaseBackupResult.Failed(DatabaseBackupFailureReason.BackupFileNotFound);
        }

        if (string.Equals(sourcePath, livePath, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(sourcePath), DatabaseBackupArtifact.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return RestoreDatabaseBackupResult.Failed(DatabaseBackupFailureReason.InvalidBackup, sourcePath);
        }

        await maintenanceLock.Semaphore.WaitAsync(cancellationToken);
        string? stagingPath = null;
        string? rollbackPath = null;
        string? preRestorePath = null;
        var swapCompleted = false;

        try
        {
            authorizationFailure = await ValidateMaintenanceAuthorizationAsync(
                initiatingUserId,
                cancellationToken);
            if (authorizationFailure != DatabaseBackupFailureReason.None)
                return RestoreDatabaseBackupResult.Failed(authorizationFailure, sourcePath);

            stagingPath = CreateUnusedPath(Path.GetDirectoryName(livePath)!, "RestoreStaging");
            var artifactFailure = await DatabaseBackupArtifact.ExtractAsync(sourcePath, stagingPath, cancellationToken);
            if (artifactFailure != DatabaseBackupFailureReason.None)
                return RestoreDatabaseBackupResult.Failed(artifactFailure, sourcePath);

            var knownMigrations = dbContext.Database.GetMigrations().ToArray();
            var stagingValidation = await ValidateDatabaseAsync(
                stagingPath,
                knownMigrations,
                requireCurrentSchema: false,
                requireActiveResponsible: false,
                cancellationToken);

            if (!stagingValidation.IsValid)
            {
                return RestoreDatabaseBackupResult.Failed(stagingValidation.FailureReason, sourcePath);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (stagingValidation.AppliedMigrations.Count < knownMigrations.Length)
            {
                try
                {
                    await MigrateStagingAsync(stagingPath, cancellationToken);
                }
                catch (Exception exception) when (exception is SqliteException or InvalidOperationException)
                {
                    logger?.LogWarning(exception, "The staging backup could not be migrated to the current schema.");
                    return RestoreDatabaseBackupResult.Failed(
                        DatabaseBackupFailureReason.IncompatibleBackup,
                        sourcePath);
                }
            }

            await CheckpointAndCloseAsync(stagingPath, cancellationToken);
            DeleteSidecarsBestEffort(stagingPath);

            var migratedValidation = await ValidateDatabaseAsync(
                stagingPath,
                knownMigrations,
                requireCurrentSchema: true,
                requireActiveResponsible: true,
                cancellationToken,
                requireAllocationIntegrity: true);

            if (!migratedValidation.IsValid)
            {
                return RestoreDatabaseBackupResult.Failed(migratedValidation.FailureReason, sourcePath);
            }

            await CheckpointAndCloseAsync(stagingPath, cancellationToken);
            DeleteSidecarsBestEffort(stagingPath);
            // Staging validation/migration may have outlived the initiating session.
            authorizationFailure = await ValidateMaintenanceAuthorizationAsync(
                initiatingUserId,
                cancellationToken);
            if (authorizationFailure != DatabaseBackupFailureReason.None)
                return RestoreDatabaseBackupResult.Failed(authorizationFailure, sourcePath);

            Directory.CreateDirectory(options.LocalBackupDirectory);
            try
            {
                // Already inside the maintenance lock: share the official artifact
                // pipeline, without re-entering the semaphore or changing authorization.
                var safety = await CreateArtifactBackupAsync(options.LocalBackupDirectory, "PreRestore",
                    requireActiveResponsible: true, cancellationToken);
                if (!safety.Success)
                    return RestoreDatabaseBackupResult.Failed(DatabaseBackupFailureReason.RestoreFailed, sourcePath);
                preRestorePath = safety.BackupPath;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
            {
                logger?.LogWarning(exception, "The safety backup required before restore could not be created.");
                DeleteDatabaseFilesBestEffort(preRestorePath);
                preRestorePath = null;
                return RestoreDatabaseBackupResult.Failed(DatabaseBackupFailureReason.StorageFailure, sourcePath);
            }

            // PreRestore also awaits I/O. Recheck before touching live WAL/handles.
            authorizationFailure = await ValidateMaintenanceAuthorizationAsync(
                initiatingUserId,
                cancellationToken);
            if (authorizationFailure != DatabaseBackupFailureReason.None)
                return RestoreDatabaseBackupResult.Failed(authorizationFailure, sourcePath, preRestorePath);

            await CheckpointAndCloseAsync(livePath, cancellationToken);
            await dbContext.Database.CloseConnectionAsync();
            SqliteConnection.ClearAllPools();
            DeleteSidecarsBestEffort(livePath);

            // Cancellation is honored until this boundary. The atomic replacement and any
            // required rollback then run to completion so cancellation cannot strand the DB.
            cancellationToken.ThrowIfCancellationRequested();

            rollbackPath = CreateUnusedPath(Path.GetDirectoryName(livePath)!, "RestoreRollback");
            try
            {
                authorizationFailure = await ValidateMaintenanceAuthorizationAsync(
                    initiatingUserId,
                    cancellationToken);
                if (authorizationFailure != DatabaseBackupFailureReason.None)
                    return RestoreDatabaseBackupResult.Failed(authorizationFailure, sourcePath, preRestorePath);

                File.Replace(stagingPath, livePath, rollbackPath, ignoreMetadataErrors: true);
                stagingPath = null;
                swapCompleted = true;
                restoreTestHook.InvokeAfterAtomicReplace(livePath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger?.LogWarning(exception, "The atomic database replacement failed.");
                return RestoreDatabaseBackupResult.Failed(
                    DatabaseBackupFailureReason.RestoreFailed,
                    sourcePath,
                    preRestorePath);
            }

            var postSwapValidation = await ValidateDatabaseAsync(
                livePath,
                knownMigrations,
                requireCurrentSchema: true,
                requireActiveResponsible: true,
                CancellationToken.None,
                requireAllocationIntegrity: true);

            if (!postSwapValidation.IsValid)
            {
                try
                {
                    RollbackAtomicReplacement(rollbackPath, livePath);
                    rollbackPath = null;
                    swapCompleted = false;
                }
                catch (Exception rollbackException)
                {
                    throw new CriticalDatabaseRestoreException(
                        "The restored database failed validation and the atomic rollback also failed.",
                        rollbackException);
                }

                return RestoreDatabaseBackupResult.Failed(
                    DatabaseBackupFailureReason.RestoreFailed,
                    sourcePath,
                    preRestorePath);
            }

            await CheckpointAndCloseAsync(livePath, CancellationToken.None);
            SqliteConnection.ClearAllPools();
            DeleteSidecarsBestEffort(livePath);
            DeleteDatabaseFilesBestEffort(rollbackPath);
            rollbackPath = null;
            swapCompleted = false;
            currentUserSession.Clear();

            return RestoreDatabaseBackupResult.Succeeded(sourcePath, preRestorePath!);
        }
        catch (CriticalDatabaseRestoreException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
            if (swapCompleted)
            {
                try
                {
                    RollbackAtomicReplacement(rollbackPath!, livePath);
                    rollbackPath = null;
                    swapCompleted = false;
                }
                catch (Exception rollbackException)
                {
                    throw new CriticalDatabaseRestoreException(
                        "The restore failed after replacement and the atomic rollback also failed.",
                        rollbackException);
                }
            }

            logger?.LogWarning(exception, "The database restore failed before completion.");
            return RestoreDatabaseBackupResult.Failed(
                DatabaseBackupFailureReason.RestoreFailed,
                sourcePath,
                preRestorePath);
        }
        catch (Exception) when (swapCompleted)
        {
            try
            {
                RollbackAtomicReplacement(rollbackPath!, livePath);
            }
            catch (Exception rollbackException)
            {
                throw new CriticalDatabaseRestoreException(
                    "An unexpected post-swap failure occurred and the atomic rollback also failed.",
                    rollbackException);
            }

            rollbackPath = null;
            swapCompleted = false;
            throw;
        }
        finally
        {
            DeleteDatabaseFilesBestEffort(stagingPath);
            if (!swapCompleted)
            {
                DeleteDatabaseFilesBestEffort(rollbackPath);
            }

            maintenanceLock.Semaphore.Release();
        }
    }

    private async Task<CreateDatabaseBackupResult> CreateBackupAsync(
        string destinationDirectory,
        bool createDirectory,
        string nameKind,
        CancellationToken cancellationToken)
    {
        var initiatingUserId = currentUserSession.UserId;
        var authorizationFailure = await ValidateMaintenanceAuthorizationAsync(
            initiatingUserId,
            cancellationToken);
        if (authorizationFailure is not DatabaseBackupFailureReason.None)
        {
            return CreateDatabaseBackupResult.Failed(authorizationFailure);
        }

        await maintenanceLock.Semaphore.WaitAsync(cancellationToken);
        try
        {
            authorizationFailure = await ValidateMaintenanceAuthorizationAsync(
                initiatingUserId,
                cancellationToken);
            if (authorizationFailure is not DatabaseBackupFailureReason.None)
            {
                return CreateDatabaseBackupResult.Failed(authorizationFailure);
            }

            if (createDirectory)
            {
                Directory.CreateDirectory(destinationDirectory);
            }
            else if (!Directory.Exists(destinationDirectory))
            {
                return CreateDatabaseBackupResult.Failed(
                    DatabaseBackupFailureReason.DestinationDirectoryNotFound);
            }

            return await CreateArtifactBackupAsync(destinationDirectory, nameKind,
                requireActiveResponsible: false, cancellationToken,
                () => ValidateMaintenanceAuthorizationAsync(initiatingUserId, cancellationToken));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or SqliteException)
        {
            logger?.LogWarning(exception, "The database backup could not be created.");
            return CreateDatabaseBackupResult.Failed(DatabaseBackupFailureReason.StorageFailure);
        }
        finally
        {
            maintenanceLock.Semaphore.Release();
        }
    }

    private async Task<StartupDatabaseBackupResult> CreateStartupBackupAsync(
        string nameKind,
        bool requireCurrentSchema,
        CancellationToken cancellationToken)
    {
        var localBusinessDate = GetLocalBusinessDate();
        await maintenanceLock.Semaphore.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(options.LocalBackupDirectory);
            var result = await CreateArtifactBackupAsync(
                options.LocalBackupDirectory,
                nameKind,
                requireActiveResponsible: false,
                cancellationToken,
                requireCurrentSchema: requireCurrentSchema);
            return ToStartupResult(result, localBusinessDate);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or SqliteException
            or InvalidOperationException)
        {
            logger?.LogWarning(exception, "The {BackupKind} database backup could not be created.", nameKind);
            return StartupDatabaseBackupResult.Failed(
                DatabaseBackupFailureReason.StorageFailure,
                localBusinessDate);
        }
        finally
        {
            maintenanceLock.Semaphore.Release();
        }
    }

    private async Task<CreateDatabaseBackupResult> CreateArtifactBackupAsync(
        string destinationDirectory,
        string nameKind,
        bool requireActiveResponsible,
        CancellationToken cancellationToken,
        Func<Task<DatabaseBackupFailureReason>>? validateBeforePublication = null,
        bool requireCurrentSchema = true)
    {
        var createdAtUtc = timeProvider.GetUtcNow();
        var token = Guid.NewGuid().ToString("N");
        var workspace = Path.Combine(destinationDirectory, $".fmbackup-{token}.tmp");
        var snapshotPath = Path.Combine(workspace, "snapshot.sqlite");
        var databasePath = Path.Combine(workspace, "database.sqlite");
        var pendingPath = Path.Combine(destinationDirectory, $".fmbackup-{token}.partial");
        var backupPath = Path.Combine(destinationDirectory,
            $"FinancialManagement_{nameKind}_{createdAtUtc:yyyyMMdd_HHmmss_fff}_{token}{DatabaseBackupArtifact.Extension}");
        try
        {
            Directory.CreateDirectory(workspace);
            BackupDatabase(GetLiveDatabasePath(), snapshotPath);
            // The Backup API has closed its destination. Move only this newly
            // generated snapshot to a fresh name, never an arbitrary restore source.
            if (File.Exists(snapshotPath + "-wal") && new FileInfo(snapshotPath + "-wal").Length != 0)
                return CreateDatabaseBackupResult.Failed(DatabaseBackupFailureReason.InvalidBackup);
            File.Move(snapshotPath, databasePath);
            var validation = await ValidateDatabaseAsync(databasePath, dbContext.Database.GetMigrations().ToArray(),
                requireCurrentSchema, requireActiveResponsible, cancellationToken);
            if (!validation.IsValid)
                return CreateDatabaseBackupResult.Failed(validation.FailureReason);
            await CheckpointAndCloseAsync(databasePath, cancellationToken);
            DeleteSidecarsBestEffort(databasePath);
            await DatabaseBackupArtifact.WriteAsync(databasePath, pendingPath, createdAtUtc, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var fileSize = new FileInfo(pendingPath).Length;
            var publicationAuthorizationFailure = validateBeforePublication is null
                ? DatabaseBackupFailureReason.None
                : await validateBeforePublication();
            if (publicationAuthorizationFailure is not DatabaseBackupFailureReason.None)
                return CreateDatabaseBackupResult.Failed(publicationAuthorizationFailure);

            // Same destination directory/filesystem, no overwrite and no copy fallback.
            // The final .fmbak name becomes visible only after ZIP/streams are closed.
            File.Move(pendingPath, backupPath, overwrite: false);
            return CreateDatabaseBackupResult.Succeeded(backupPath, createdAtUtc, fileSize);
        }
        catch (InvalidDataException)
        {
            return CreateDatabaseBackupResult.Failed(DatabaseBackupFailureReason.InvalidBackup);
        }
        finally
        {
            DeleteFileBestEffort(pendingPath);
            DeleteDatabaseFilesBestEffort(snapshotPath);
            DeleteDatabaseFilesBestEffort(databasePath);
            try { Directory.Delete(workspace); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
    }

    private DateOnly GetLocalBusinessDate()
    {
        var localDateTime = TimeZoneInfo.ConvertTime(
            timeProvider.GetUtcNow(),
            timeProvider.LocalTimeZone);
        return DateOnly.FromDateTime(localDateTime.DateTime);
    }

    private static StartupDatabaseBackupResult ToStartupResult(
        CreateDatabaseBackupResult result,
        DateOnly localBusinessDate) =>
        result.Success && result.BackupPath is not null
            ? StartupDatabaseBackupResult.Succeeded(result.BackupPath, localBusinessDate)
            : StartupDatabaseBackupResult.Failed(result.FailureReason, localBusinessDate);

    private async Task<DatabaseBackupFailureReason> ValidateMaintenanceAuthorizationAsync(
        long? initiatingUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            if (currentUserSession.UserId is not long currentUserId)
                return DatabaseBackupFailureReason.NotAuthenticated;
            if (currentUserId != initiatingUserId)
                return DatabaseBackupFailureReason.Forbidden;

            var failure = await GetAuthorizationFailureAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (failure is not DatabaseBackupFailureReason.None)
                return failure;
            if (currentUserSession.UserId is null)
                return DatabaseBackupFailureReason.NotAuthenticated;
            if (currentUserSession.UserId != initiatingUserId)
                return DatabaseBackupFailureReason.Forbidden;
            return DatabaseBackupFailureReason.None;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger?.LogWarning(exception, "Database maintenance authorization could not be validated.");
            return DatabaseBackupFailureReason.Forbidden;
        }
    }

    private async Task<DatabaseBackupFailureReason> GetAuthorizationFailureAsync(
        CancellationToken cancellationToken)
    {
        if (!currentUserSession.IsAuthenticated)
        {
            return DatabaseBackupFailureReason.NotAuthenticated;
        }

        return await authorizationService.CanAccessResponsibleCapabilityAsync(
            ResponsibleCapability.BackupRestore,
            cancellationToken)
            ? DatabaseBackupFailureReason.None
            : DatabaseBackupFailureReason.Forbidden;
    }

    private string GetLiveDatabasePath()
    {
        var builder = new SqliteConnectionStringBuilder(dbContext.Database.GetConnectionString());
        if (string.IsNullOrWhiteSpace(builder.DataSource) || builder.DataSource == ":memory:")
        {
            throw new InvalidOperationException("Backup and restore require a file-backed SQLite database.");
        }

        return Path.GetFullPath(builder.DataSource);
    }

    private static void BackupDatabase(string sourcePath, string destinationPath)
    {
        using var source = new SqliteConnection(BuildConnectionString(sourcePath, SqliteOpenMode.ReadWrite));
        using var destination = new SqliteConnection(BuildConnectionString(destinationPath, SqliteOpenMode.ReadWriteCreate));
        source.Open();
        destination.Open();
        source.BackupDatabase(destination);
    }

    private async Task<DatabaseValidation> ValidateDatabaseAsync(
        string databasePath,
        IReadOnlyList<string> knownMigrations,
        bool requireCurrentSchema,
        bool requireActiveResponsible,
        CancellationToken cancellationToken,
        bool requireAllocationIntegrity = false)
    {
        try
        {
            await using var connection = new SqliteConnection(
                BuildConnectionString(databasePath, SqliteOpenMode.ReadOnly));
            await connection.OpenAsync(cancellationToken);

            if (!string.Equals(
                    await ExecuteScalarStringAsync(connection, "PRAGMA integrity_check;", cancellationToken),
                    "ok",
                    StringComparison.OrdinalIgnoreCase))
            {
                return DatabaseValidation.Invalid(DatabaseBackupFailureReason.InvalidBackup);
            }

            await using (var foreignKeyCheck = connection.CreateCommand())
            {
                foreignKeyCheck.CommandText = "PRAGMA foreign_key_check;";
                await using var reader = await foreignKeyCheck.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    return DatabaseValidation.Invalid(DatabaseBackupFailureReason.InvalidBackup);
                }
            }

            var schemaValidator = new DatabaseSchemaValidator(dbContext, logger);
            if (!await schemaValidator.ValidateHistoryAsync(connection, cancellationToken))
            {
                return DatabaseValidation.Invalid(DatabaseBackupFailureReason.InvalidBackup);
            }

            var appliedMigrations = new List<string>();
            await using (var history = connection.CreateCommand())
            {
                history.CommandText = $"SELECT MigrationId FROM \"{MigrationHistoryTable}\" ORDER BY MigrationId;";
                await using var reader = await history.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                {
                    appliedMigrations.Add(reader.GetString(0));
                }
            }

            if (appliedMigrations.Count == 0 ||
                appliedMigrations.Count > knownMigrations.Count ||
                !appliedMigrations.SequenceEqual(knownMigrations.Take(appliedMigrations.Count), StringComparer.Ordinal))
            {
                return DatabaseValidation.Invalid(DatabaseBackupFailureReason.IncompatibleBackup);
            }

            if (requireCurrentSchema && appliedMigrations.Count != knownMigrations.Count)
            {
                return DatabaseValidation.Invalid(DatabaseBackupFailureReason.IncompatibleBackup);
            }

            if (!await schemaValidator.ValidateAsync(connection, appliedMigrations, cancellationToken))
            {
                logger?.LogWarning("The database schema does not match migration {MigrationId}.", appliedMigrations[^1]);
                return DatabaseValidation.Invalid(DatabaseBackupFailureReason.InvalidBackup);
            }

            // Before staging migration, after migration and after live swap.
            // Also reject malformed backup snapshots; never rewrite their amounts.
            if (!await FinancialStorageValidator.ValidateAsync(connection, allowEmpty: false, cancellationToken))
                return DatabaseValidation.Invalid(DatabaseBackupFailureReason.InvalidBackup);

            if (requireAllocationIntegrity)
            {
                // A structurally valid backup may still contain impossible sums.
                // Validate the complete staged/current database without repairing it.
                var validationOptions = new DbContextOptionsBuilder<FinancialManagementDbContext>()
                    .UseSqlite(connection).Options;
                await using var validationContext = new FinancialManagementDbContext(validationOptions);
                var saleIds = (await validationContext.Sales.Select(item => item.Id)
                    .ToListAsync(cancellationToken)).ToHashSet();
                var receivableIds = (await validationContext.InitialCustomerReceivables.Select(item => item.Id)
                    .ToListAsync(cancellationToken)).ToHashSet();
                var purchaseIds = (await validationContext.Purchases.Select(item => item.Id)
                    .ToListAsync(cancellationToken)).ToHashSet();
                var payableIds = (await validationContext.InitialSupplierPayables.Select(item => item.Id)
                    .ToListAsync(cancellationToken)).ToHashSet();
                if (await AllocationIntegrity.ReadCustomerAsync(validationContext, saleIds, receivableIds,
                        null, cancellationToken) is null
                    || await AllocationIntegrity.ReadSupplierAsync(validationContext, purchaseIds, payableIds,
                        null, cancellationToken) is null)
                {
                    return DatabaseValidation.Invalid(DatabaseBackupFailureReason.InvalidBackup);
                }
            }

            if (requireActiveResponsible)
            {
                await using var responsible = connection.CreateCommand();
                responsible.CommandText =
                    "SELECT EXISTS(SELECT 1 FROM Users WHERE Role = $role AND IsActive = 1);";
                responsible.Parameters.AddWithValue("$role", UserRole.Responsable.ToString());
                if (Convert.ToInt64(await responsible.ExecuteScalarAsync(cancellationToken)) != 1)
                {
                    return DatabaseValidation.Invalid(DatabaseBackupFailureReason.InvalidBackup);
                }
            }

            return DatabaseValidation.Valid(appliedMigrations);
        }
        catch (SqliteException exception)
        {
            logger?.LogWarning(exception, "SQLite validation failed for {DatabasePath}.", databasePath);
            return DatabaseValidation.Invalid(DatabaseBackupFailureReason.InvalidBackup);
        }
    }

    private static async Task MigrateStagingAsync(string stagingPath, CancellationToken cancellationToken)
    {
        var options = new DbContextOptionsBuilder<FinancialManagementDbContext>()
            .UseSqlite(BuildConnectionString(stagingPath, SqliteOpenMode.ReadWrite))
            .AddInterceptors(SqlitePragmaConnectionInterceptor.Instance)
            .Options;

        await using var stagingContext = new FinancialManagementDbContext(options);
        await stagingContext.Database.MigrateAsync(cancellationToken);
        await stagingContext.Database.CloseConnectionAsync();
    }

    private static async Task CheckpointAndCloseAsync(string databasePath, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(
            BuildConnectionString(databasePath, SqliteOpenMode.ReadWrite));
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        await command.ExecuteNonQueryAsync(cancellationToken);
        await connection.CloseAsync();
    }

    private static void RollbackAtomicReplacement(string rollbackPath, string livePath)
    {
        SqliteConnection.ClearAllPools();
        DeleteSidecarsBestEffort(livePath);
        File.Replace(rollbackPath, livePath, null, ignoreMetadataErrors: true);
    }

    private string CreateUniquePath(
        string directory,
        string nameKind,
        DateTimeOffset? timestamp = null)
    {
        var createdAtUtc = timestamp ?? timeProvider.GetUtcNow();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var token = RandomNumberGenerator.GetHexString(8);
            var fileName = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"FinancialManagement_{nameKind}_{createdAtUtc:yyyyMMdd_HHmmss_fff}_{token}.db");
            var path = Path.Combine(directory, fileName);

            try
            {
                using var reservation = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return path;
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }

        throw new IOException("A unique database maintenance file name could not be reserved.");
    }

    private string CreateUnusedPath(string directory, string nameKind)
    {
        var reservedPath = CreateUniquePath(directory, nameKind);
        File.Delete(reservedPath);
        return reservedPath;
    }

    private static string BuildConnectionString(string databasePath, SqliteOpenMode mode) =>
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = mode,
            Pooling = false,
            ForeignKeys = true,
            DefaultTimeout = 5
        }.ToString();

    private static async Task<string?> ExecuteScalarStringAsync(
        SqliteConnection connection,
        string commandText,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = commandText;
        return Convert.ToString(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static void DeleteSidecarsBestEffort(string databasePath)
    {
        DeleteFileBestEffort(databasePath + "-wal");
        DeleteFileBestEffort(databasePath + "-shm");
        DeleteFileBestEffort(databasePath + "-journal");
    }

    private static void DeleteDatabaseFilesBestEffort(string? databasePath)
    {
        if (databasePath is null)
        {
            return;
        }

        DeleteFileBestEffort(databasePath);
        DeleteSidecarsBestEffort(databasePath);
    }

    private static void DeleteFileBestEffort(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }

    private sealed record DatabaseValidation(
        bool IsValid,
        DatabaseBackupFailureReason FailureReason,
        IReadOnlyList<string> AppliedMigrations)
    {
        public static DatabaseValidation Valid(IReadOnlyList<string> appliedMigrations) =>
            new(true, DatabaseBackupFailureReason.None, appliedMigrations);

        public static DatabaseValidation Invalid(DatabaseBackupFailureReason failureReason) =>
            new(false, failureReason, Array.Empty<string>());
    }

    private sealed class CriticalDatabaseRestoreException(string message, Exception innerException)
        : Exception(message, innerException);
}
