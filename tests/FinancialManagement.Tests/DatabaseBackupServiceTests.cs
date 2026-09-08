using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Backup;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using FinancialManagement.Infrastructure.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Xunit.Abstractions;

namespace FinancialManagement.Tests;

public sealed partial class DatabaseBackupServiceTests(ITestOutputHelper output)
{
    private static readonly DateTimeOffset TestUtc = new(2026, 9, 4, 12, 30, 45, TimeSpan.Zero);

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    public async Task Backup_WalOnlyCommitProducesSelfContainedSnapshot(int iteration)
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        await fixture.Context.Database.OpenConnectionAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint=0;");
        await fixture.AddRepresentativeDataAsync($"Gate-{iteration}");
        Assert.True(new FileInfo(fixture.LivePath + "-wal").Length > 32);
        var oracle = Path.Combine(fixture.Root, "main-only-oracle.db");
        File.Copy(fixture.LivePath, oracle);
        Assert.Equal(0, await ScalarLongAsync(oracle, "SELECT COUNT(*) FROM Sales;"));

        var backup = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(backup.Success);
        Assert.False(File.Exists(backup.BackupPath + "-wal"));
        Assert.False(File.Exists(backup.BackupPath + "-shm"));
        var standalone = Path.Combine(Directory.CreateDirectory(Path.Combine(fixture.Root, "isolated")).FullName, "database.sqlite");
        File.Copy(await ExtractForInspectionAsync(backup.BackupPath!), standalone);
        Assert.False(File.Exists(standalone + "-wal"));
        Assert.False(File.Exists(standalone + "-shm"));
        Assert.Equal(125_075, await ScalarLongAsync(standalone, "SELECT TotalAmountMinor FROM Sales;"));
        Assert.Equal("ok", await ScalarStringAsync(standalone, "PRAGMA integrity_check;"));
        Assert.Equal(0, await ScalarLongAsync(standalone, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
    }

    [Fact]
    public async Task Operations_RequireAnAuthenticatedResponsable()
    {
        await using var fixture = await BackupFixture.CreateAsync();

        var unauthenticated = await fixture.Service.CreateLocalBackupAsync();
        Assert.False(unauthenticated.Success);
        Assert.Equal(DatabaseBackupFailureReason.NotAuthenticated, unauthenticated.FailureReason);

        await fixture.AddEmployeeWithEveryPermissionAsync();
        await fixture.AuthenticateAsync("employee", "Password-123!");

        var employeeBackup = await fixture.Service.CreateLocalBackupAsync();
        var employeeRestore = await fixture.Service.RestoreAsync(new(fixture.LivePath));
        Assert.Equal(DatabaseBackupFailureReason.Forbidden, employeeBackup.FailureReason);
        Assert.Equal(DatabaseBackupFailureReason.Forbidden, employeeRestore.FailureReason);
    }

    [Fact]
    public async Task LocalAndExternalBackups_UseDistinctNamesAndRespectDestinationRules()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        Assert.False(Directory.Exists(fixture.BackupDirectory));

        var first = await fixture.Service.CreateLocalBackupAsync();
        var second = await fixture.Service.CreateLocalBackupAsync();

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEqual(first.BackupPath, second.BackupPath);
        Assert.True(File.Exists(first.BackupPath));
        Assert.True(first.FileSizeBytes > 0);
        Assert.Equal(".fmbak", Path.GetExtension(first.BackupPath));
        Assert.Empty(Directory.GetDirectories(fixture.BackupDirectory));
        Assert.All(Directory.GetFiles(fixture.BackupDirectory), path => Assert.EndsWith(".fmbak", path));
        Assert.Equal(TestUtc, first.CreatedAtUtc);
        Assert.StartsWith("FinancialManagement_Backup_20260904_123045_000_", first.FileName);

        var externalDirectory = Directory.CreateDirectory(Path.Combine(fixture.Root, "external")).FullName;
        var external = await fixture.Service.CreateExternalBackupAsync(new(externalDirectory));
        Assert.True(external.Success);
        Assert.Equal(externalDirectory, Path.GetDirectoryName(external.BackupPath));

        var missingDirectory = Path.Combine(fixture.Root, "missing-external");
        var missing = await fixture.Service.CreateExternalBackupAsync(new(missingDirectory));
        Assert.Equal(DatabaseBackupFailureReason.DestinationDirectoryNotFound, missing.FailureReason);
        Assert.False(Directory.Exists(missingDirectory));
    }

    [Fact]
    public async Task BackupDatabase_IsWalSafeStandaloneAndContainsCompleteFinancialData()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        await fixture.Context.Database.OpenConnectionAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint = 0;");
        await fixture.AddRepresentativeDataAsync("A");

        Assert.True(File.Exists(fixture.LivePath + "-wal"));
        var result = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(result.Success);

        var databasePath = await ExtractForInspectionAsync(result.BackupPath!);
        await using var backup = fixture.CreateIndependentContext(databasePath);
        Assert.Equal("ok", await ScalarStringAsync(databasePath, "PRAGMA integrity_check;"));
        Assert.Equal(0L, await ScalarLongAsync(databasePath, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        Assert.Equal(4L, await ScalarLongAsync(databasePath, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        Assert.Equal(1, await backup.Clients.CountAsync(client => client.Name == "Client A"));
        Assert.Equal(125_075, await backup.Sales.Where(sale => sale.Reference == "SALE-A").Select(sale => sale.TotalAmountMinor).SingleAsync());
        Assert.Equal(80_000, await backup.Purchases.Where(purchase => purchase.Reference == "PURCHASE-A").Select(purchase => purchase.TotalAmountMinor).SingleAsync());
        Assert.Equal(1, await backup.CustomerPayments.CountAsync(payment => payment.Reference == "CP-A"));
        Assert.Equal(1, await backup.SupplierPayments.CountAsync(payment => payment.Reference == "SP-A"));
        Assert.Equal(1, await backup.Expenses.CountAsync(expense => expense.Reference == "EXP-A"));
        Assert.Equal(1, await backup.TreasuryOpeningBalances.CountAsync());
        Assert.False(File.Exists(result.BackupPath + "-wal"));
        Assert.False(File.Exists(result.BackupPath + "-shm"));
    }

    [Fact]
    public async Task Restore_ReplacesWholeDatabasePreservesSafetyBackupAndClearsSession()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        await fixture.AddRepresentativeDataAsync("A");
        var backupA = await fixture.Service.CreateLocalBackupAsync();
        var sourceHashBefore = await HashFileAsync(backupA.BackupPath!);

        fixture.Context.Clients.Add(new Client
        {
            Name = "Client B",
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        });
        await fixture.Context.SaveChangesAsync();

        var result = await fixture.Service.RestoreAsync(new(backupA.BackupPath!));

        Assert.True(result.Success);
        Assert.True(result.RestartRequired);
        Assert.Equal(backupA.BackupPath, result.RestoredFromPath);
        Assert.True(File.Exists(result.PreRestoreBackupPath));
        Assert.False(fixture.Session.IsAuthenticated);
        Assert.Equal(sourceHashBefore, await HashFileAsync(backupA.BackupPath!));

        await using var restored = fixture.CreateIndependentContext(fixture.LivePath);
        Assert.Equal(1, await restored.Clients.CountAsync(client => client.Name == "Client A"));
        Assert.Equal(0, await restored.Clients.CountAsync(client => client.Name == "Client B"));

        var safetyPath = await ExtractForInspectionAsync(result.PreRestoreBackupPath!);
        await using var safety = fixture.CreateIndependentContext(safetyPath);
        Assert.Equal(1, await safety.Clients.CountAsync(client => client.Name == "Client B"));
        Assert.Equal("ok", await ScalarStringAsync(safetyPath, "PRAGMA integrity_check;"));
    }

    [Fact]
    public async Task Restore_DoesNotReplayStaleLiveWal()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("State A"));
        await fixture.Context.SaveChangesAsync();
        var backupA = await fixture.Service.CreateLocalBackupAsync();

        await fixture.Context.Database.OpenConnectionAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint = 0;");
        fixture.Context.Clients.Add(NewClient("State B in WAL"));
        await fixture.Context.SaveChangesAsync();
        Assert.True(File.Exists(fixture.LivePath + "-wal"));

        var result = await fixture.Service.RestoreAsync(new(backupA.BackupPath!));
        Assert.True(result.Success);

        await using var restored = fixture.CreateIndependentContext(fixture.LivePath);
        Assert.Equal(1, await restored.Clients.CountAsync(client => client.Name == "State A"));
        Assert.Equal(0, await restored.Clients.CountAsync(client => client.Name == "State B in WAL"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_IncludesCommittedSourceWalWithoutChangingArtifact(bool commitAfterRead)
    {
        await using var source = await BackupFixture.CreateAsync(authenticate: true);
        await using var destination = await BackupFixture.CreateAsync(authenticate: true);
        destination.Context.Clients.Add(NewClient("Destination B"));
        await destination.Context.SaveChangesAsync();

        await source.Context.Database.OpenConnectionAsync();
        await source.Context.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE);");
        await source.Context.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint = 0;");
        var userId = await source.Context.Users.Select(user => user.Id).SingleAsync();
        var client = NewClient("Source A");
        source.Context.Clients.Add(client);
        await source.Context.SaveChangesAsync();
        source.Context.Sales.Add(new Sale
        {
            Reference = "ASTRA-001-WAL-COMMIT",
            ClientId = client.Id,
            SaleDate = new DateOnly(2026, 9, 4),
            TotalAmountMinor = 123,
            CreatedByUserId = userId,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        });
        await source.Context.SaveChangesAsync();
        Assert.Equal(123, await source.Context.Sales.AsNoTracking()
            .Where(sale => sale.Reference == "ASTRA-001-WAL-COMMIT")
            .Select(sale => sale.TotalAmountMinor).SingleAsync());

        if (commitAfterRead)
        {
            // Leave the latest commit beyond the existing SHM read mark. A plain
            // read-only database connection may otherwise appear immutable by chance.
            await source.Context.Database.ExecuteSqlRawAsync(
                "UPDATE Sales SET Observation = 'Committed after read' WHERE Reference = 'ASTRA-001-WAL-COMMIT';");
        }

        Assert.True(new FileInfo(source.LivePath + "-wal").Length > 32);
        Assert.True(new FileInfo(source.LivePath + "-shm").Length > 0);
        // Test oracle only: demonstrate that copying the main file would omit the sale.
        var mainOnlyPath = Path.Combine(source.Root, "main-only-oracle.db");
        File.Copy(source.LivePath, mainOnlyPath);
        await using (var mainOnly = source.CreateIndependentContext(mainOnlyPath))
        {
            Assert.False(await mainOnly.Sales.AnyAsync(sale => sale.Reference == "ASTRA-001-WAL-COMMIT"));
        }

        var backup = await source.Service.CreateLocalBackupAsync();
        Assert.True(backup.Success);
        var artifactPath = Path.Combine(destination.Root, "wal-snapshot.fmbak");
        File.Copy(backup.BackupPath!, artifactPath);
        // Restore cannot consult the source database or its WAL: close and remove
        // the synthetic live source after the completed artifact has been copied.
        await source.Context.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(source.LivePath + suffix);
        var hashBefore = await HashFileAsync(artifactPath);
        var result = await destination.Service.RestoreAsync(new(artifactPath));

        Assert.True(result.Success);
        Assert.True(result.RestartRequired);
        var hashAfter = await HashFileAsync(artifactPath);
        output.WriteLine($"artifact before={Convert.ToHexString(hashBefore)}; after={Convert.ToHexString(hashAfter)}");
        Assert.Equal(hashBefore, hashAfter);

        await using var restored = destination.CreateIndependentContext(destination.LivePath);
        var restoredSale = await restored.Sales.AsNoTracking().SingleAsync(sale => sale.Reference == "ASTRA-001-WAL-COMMIT");
        Assert.Equal(123, restoredSale.TotalAmountMinor);
        Assert.Equal(commitAfterRead ? "Committed after read" : null, restoredSale.Observation);
        Assert.False(await restored.Clients.AnyAsync(item => item.Name == "Destination B"));
        Assert.Equal("ok", await ScalarStringAsync(destination.LivePath, "PRAGMA integrity_check;"));
        Assert.Equal(0, await ScalarLongAsync(destination.LivePath, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        Assert.Equal(source.Context.Database.GetMigrations().ToArray(),
            (await restored.Database.GetAppliedMigrationsAsync()).ToArray());
    }

    [Fact]
    public async Task Restore_RollsBackAtomicallyWhenPostSwapValidationFails()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Candidate A"));
        await fixture.Context.SaveChangesAsync();
        var backupA = await fixture.Service.CreateLocalBackupAsync();

        await fixture.Context.Database.OpenConnectionAsync();
        await fixture.Context.Database.ExecuteSqlRawAsync("PRAGMA wal_autocheckpoint = 0;");
        var candidateA = await fixture.Context.Clients.SingleAsync(client => client.Name == "Candidate A");
        fixture.Context.Clients.Remove(candidateA);
        fixture.Context.Clients.Add(NewClient("Live B"));
        await fixture.Context.SaveChangesAsync();
        Assert.True(File.Exists(fixture.LivePath + "-wal"));

        var candidateWasObservedAfterSwap = false;
        fixture.RestoreTestHook.AfterAtomicReplace = livePath =>
        {
            candidateWasObservedAfterSwap =
                ScalarLongSynchronously(livePath, "SELECT COUNT(*) FROM Clients WHERE Name='Candidate A';") == 1 &&
                ScalarLongSynchronously(livePath, "SELECT COUNT(*) FROM Clients WHERE Name='Live B';") == 0;
            ExecuteSynchronously(livePath, "DROP INDEX UX_TreasuryOpeningBalance_Singleton;");
        };

        var result = await fixture.Service.RestoreAsync(new(backupA.BackupPath!));

        Assert.False(result.Success);
        Assert.True(candidateWasObservedAfterSwap);
        Assert.Equal(DatabaseBackupFailureReason.RestoreFailed, result.FailureReason);
        Assert.False(result.RestartRequired);
        Assert.True(File.Exists(result.PreRestoreBackupPath));
        Assert.True(fixture.Session.IsAuthenticated);
        Assert.False(File.Exists(fixture.LivePath + "-wal"));
        Assert.False(File.Exists(fixture.LivePath + "-shm"));

        await using var restoredLive = fixture.CreateIndependentContext(fixture.LivePath);
        Assert.Equal(0, await restoredLive.Clients.CountAsync(client => client.Name == "Candidate A"));
        Assert.Equal(1, await restoredLive.Clients.CountAsync(client => client.Name == "Live B"));
        Assert.Equal("ok", await ScalarStringAsync(fixture.LivePath, "PRAGMA integrity_check;"));
        Assert.Equal(1L, await ScalarLongAsync(fixture.LivePath,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='UX_TreasuryOpeningBalance_Singleton';"));

        await using var safety = fixture.CreateIndependentContext(await ExtractForInspectionAsync(result.PreRestoreBackupPath!));
        Assert.Equal(1, await safety.Clients.CountAsync(client => client.Name == "Live B"));
    }

    [Fact]
    public async Task Restore_RejectsInvalidForeignFutureAndInconsistentBackupsWithoutChangingLive()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();

        var missing = await fixture.Service.RestoreAsync(new(Path.Combine(fixture.Root, "missing.db")));
        Assert.Equal(DatabaseBackupFailureReason.BackupFileNotFound, missing.FailureReason);

        var textPath = Path.Combine(fixture.Root, "text.db");
        await File.WriteAllTextAsync(textPath, "not sqlite");
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, textPath, DatabaseBackupFailureReason.InvalidBackup);

        var foreignPath = Path.Combine(fixture.Root, "foreign.db");
        await ExecuteAsync(foreignPath, "CREATE TABLE Test(Id INTEGER PRIMARY KEY);");
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, foreignPath, DatabaseBackupFailureReason.InvalidBackup);

        var futurePath = Path.Combine(fixture.Root, "future.db");
        await CreateApplicationDatabaseAsync(futurePath, migrationCount: 3, activeResponsible: true);
        await ExecuteAsync(futurePath,
            "INSERT INTO __EFMigrationsHistory(MigrationId, ProductVersion) VALUES ('99999999999999_004_Future', '99.0.0');");
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, futurePath, DatabaseBackupFailureReason.IncompatibleBackup);

        var inconsistentPath = Path.Combine(fixture.Root, "inconsistent.db");
        await CreateApplicationDatabaseAsync(inconsistentPath, migrationCount: 3, activeResponsible: true);
        await ExecuteAsync(inconsistentPath,
            "DELETE FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_002_%';");
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, inconsistentPath, DatabaseBackupFailureReason.IncompatibleBackup);

        Assert.True(fixture.Session.IsAuthenticated);
        Assert.Empty(Directory.Exists(fixture.BackupDirectory)
            ? Directory.GetFiles(fixture.BackupDirectory, "*PreRestore*")
            : []);
    }

    [Fact]
    public async Task Restore_MigratesValidVersion002StagingWithoutChangingSource()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var oldBackup = Path.Combine(fixture.Root, "old-002.db");
        await CreateApplicationDatabaseAsync(oldBackup, migrationCount: 2, activeResponsible: true, clientName: "Old data");
        var artifactPath = await PackDatabaseAsync(oldBackup);
        var hashBefore = await HashFileAsync(artifactPath);

        var result = await fixture.Service.RestoreAsync(new(artifactPath));
        Assert.True(result.Success);
        Assert.Equal(hashBefore, await HashFileAsync(artifactPath));
        Assert.Equal(2L, await ScalarLongAsync(oldBackup, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        Assert.Equal(4L, await ScalarLongAsync(fixture.LivePath, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        Assert.Equal(1L, await ScalarLongAsync(fixture.LivePath,
            "SELECT COUNT(*) FROM sqlite_master WHERE type='index' AND name='UX_TreasuryOpeningBalance_Singleton';"));
        Assert.Equal(1L, await ScalarLongAsync(fixture.LivePath, "SELECT COUNT(*) FROM Clients WHERE Name='Old data';"));
    }

    [Fact]
    public async Task Restore_RejectsVersion002WhoseDataCannotSatisfyMigration003()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();
        var oldBackup = Path.Combine(fixture.Root, "old-duplicate-openings.db");
        await CreateApplicationDatabaseAsync(oldBackup, migrationCount: 2, activeResponsible: true);
        await ExecuteAsync(oldBackup,
            "INSERT INTO TreasuryOpeningBalance(OpeningDate,CashAmountMinor,BankAmountMinor,CreatedByUserId,CreatedAtUtc) " +
            "VALUES ('2026-01-01',0,0,1,'2026-01-01 00:00:00+00:00'),('2026-01-02',0,0,1,'2026-01-02 00:00:00+00:00');");

        var result = await fixture.Service.RestoreAsync(new(await PackDatabaseAsync(oldBackup)));
        Assert.Equal(DatabaseBackupFailureReason.IncompatibleBackup, result.FailureReason);
        await fixture.AssertLiveMarkerAsync();
        Assert.Equal(2L, await ScalarLongAsync(oldBackup, "SELECT COUNT(*) FROM TreasuryOpeningBalance;"));
    }

    [Fact]
    public async Task Restore_RejectsTamperedCurrentSchemaAndDatabaseWithoutActiveResponsable()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();

        var tampered = Path.Combine(fixture.Root, "tampered.db");
        await CreateApplicationDatabaseAsync(tampered, migrationCount: 3, activeResponsible: true);
        await ExecuteAsync(tampered, "DROP INDEX UX_TreasuryOpeningBalance_Singleton;");
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, tampered, DatabaseBackupFailureReason.InvalidBackup);

        var lockedOut = Path.Combine(fixture.Root, "no-active-responsable.db");
        await CreateApplicationDatabaseAsync(lockedOut, migrationCount: 3, activeResponsible: false);
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, lockedOut, DatabaseBackupFailureReason.InvalidBackup);
        Assert.True(fixture.Session.IsAuthenticated);
    }

    [Fact]
    public async Task CancellationBeforeWork_LeavesLiveAndDestinationsUntouched()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();
        var source = await fixture.Service.CreateLocalBackupAsync();
        var filesBefore = Directory.GetFiles(fixture.BackupDirectory).Order().ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.CreateLocalBackupAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => fixture.Service.RestoreAsync(new(source.BackupPath!), cancellation.Token));

        Assert.Equal(filesBefore, Directory.GetFiles(fixture.BackupDirectory).Order().ToArray());
        await fixture.AssertLiveMarkerAsync();
        Assert.True(fixture.Session.IsAuthenticated);
    }

    [Fact]
    public async Task Restore_RejectsTheLiveDatabaseAsItsOwnSource()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var result = await fixture.Service.RestoreAsync(new(fixture.LivePath.ToUpperInvariant()));
        Assert.Equal(DatabaseBackupFailureReason.InvalidBackup, result.FailureReason);
        Assert.True(fixture.Session.IsAuthenticated);
    }

    [Fact]
    public async Task InvalidBackupOutput_IsDeletedAfterValidationFailure()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        await ExecuteAsync(fixture.LivePath, "DROP INDEX UX_TreasuryOpeningBalance_Singleton;");

        var result = await fixture.Service.CreateLocalBackupAsync();

        Assert.False(result.Success);
        Assert.Equal(DatabaseBackupFailureReason.InvalidBackup, result.FailureReason);
        Assert.Empty(Directory.GetFiles(fixture.BackupDirectory));
    }

    [Fact]
    public async Task PreRestoreStorageFailure_LeavesLiveAndSessionUntouchedAndReleasesLock()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Candidate A"));
        await fixture.Context.SaveChangesAsync();
        var external = Directory.CreateDirectory(Path.Combine(fixture.Root, "restore-source")).FullName;
        var source = await fixture.Service.CreateExternalBackupAsync(new(external));
        fixture.Context.Clients.Add(NewClient("Live B"));
        await fixture.Context.SaveChangesAsync();
        await File.WriteAllTextAsync(fixture.BackupDirectory, "blocks directory creation");
        var swapWasInvoked = false;
        fixture.RestoreTestHook.AfterAtomicReplace = _ => swapWasInvoked = true;

        var result = await fixture.Service.RestoreAsync(new(source.BackupPath!));

        Assert.False(result.Success);
        Assert.False(swapWasInvoked);
        Assert.Equal(DatabaseBackupFailureReason.RestoreFailed, result.FailureReason);
        Assert.Null(result.PreRestoreBackupPath);
        Assert.True(fixture.Session.IsAuthenticated);
        await using (var live = fixture.CreateIndependentContext(fixture.LivePath))
        {
            Assert.Equal(1, await live.Clients.CountAsync(client => client.Name == "Live B"));
        }

        File.Delete(fixture.BackupDirectory);
        var operationAfterFailure = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(operationAfterFailure.Success);
    }

    private static Client NewClient(string name) => new()
    {
        Name = name,
        CreatedAtUtc = TestUtc,
        UpdatedAtUtc = TestUtc
    };

    [Theory]
    [InlineData("hash")]
    [InlineData("truncated")]
    [InlineData("missing-manifest")]
    [InlineData("missing-database")]
    [InlineData("duplicate-manifest")]
    [InlineData("duplicate-database")]
    [InlineData("../escape.sqlite")]
    [InlineData("..\\escape.sqlite")]
    [InlineData("/escape.sqlite")]
    [InlineData("C:\\escape.sqlite")]
    [InlineData("extra-entry")]
    [InlineData("version")]
    [InlineData("format")]
    [InlineData("length")]
    [InlineData("database-name")]
    [InlineData("duplicate-field")]
    [InlineData("oversized-manifest")]
    [InlineData("compressed")]
    [InlineData("invalid-json")]
    [InlineData("invalid-hash")]
    [InlineData("directory-count")]
    [InlineData("directory-size")]
    public async Task Restore_RejectsMalformedArtifactsBeforeSwap(string fault)
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();
        var valid = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(valid.Success);
        var databasePath = await ExtractForInspectionAsync(valid.BackupPath!);
        var database = await File.ReadAllBytesAsync(databasePath);
        var manifestBytes = ManifestFor(database);
        var manifest = JsonNode.Parse(manifestBytes)!;
        switch (fault)
        {
            case "hash": database[0] ^= 0xff; break;
            case "version": manifest["BackupFormatVersion"] = 2; break;
            case "format": manifest["BackupFormat"] = "OtherApplication"; break;
            case "length": manifest["DatabaseLength"] = database.LongLength + 1; break;
            case "database-name": manifest["DatabaseFileName"] = "../elsewhere.db"; break;
            case "invalid-hash": manifest["DatabaseSha256"] = new string('Z', 64); break;
        }
        manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);
        if (fault == "duplicate-field")
            manifestBytes = System.Text.Encoding.UTF8.GetBytes("{\"BackupFormatVersion\":1," + System.Text.Encoding.UTF8.GetString(manifestBytes)[1..]);
        if (fault == "oversized-manifest") manifestBytes = new byte[16 * 1024 + 1];
        if (fault == "invalid-json") manifestBytes = "not-json"u8.ToArray();
        var entries = new List<(string Name, byte[] Bytes)> { ("database.sqlite", database), ("manifest.json", manifestBytes) };
        if (fault == "missing-manifest") entries.RemoveAt(1);
        if (fault == "missing-database") entries.RemoveAt(0);
        if (fault == "duplicate-manifest") entries.Add(("manifest.json", manifestBytes));
        if (fault == "duplicate-database") entries.Add(("database.sqlite", database));
        if (fault.Contains("escape.sqlite", StringComparison.Ordinal)) entries[0] = (fault, database);
        if (fault == "extra-entry") entries.Add(("unexpected.txt", [1]));
        var artifact = Path.Combine(fixture.Root, "invalid.fmbak");
        await WriteArchiveAsync(artifact, entries, fault == "compressed" ? CompressionLevel.SmallestSize : CompressionLevel.NoCompression);
        if (fault == "truncated")
        {
            using var file = File.OpenWrite(artifact);
            file.SetLength(file.Length / 2);
        }
        if (fault == "directory-count" || fault == "directory-size")
        {
            var bytes = await File.ReadAllBytesAsync(artifact);
            bytes[bytes.Length - 22 + (fault == "directory-count" ? 10 : 12)] ^= 0xff;
            await File.WriteAllBytesAsync(artifact, bytes);
        }
        var filesBefore = Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray();
        var liveBefore = await HashFileAsync(fixture.LivePath);
        var swap = false;
        fixture.RestoreTestHook.AfterAtomicReplace = _ => swap = true;
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, artifact,
            fault == "version" ? DatabaseBackupFailureReason.IncompatibleBackup : DatabaseBackupFailureReason.InvalidBackup);
        Assert.False(swap);
        Assert.True(fixture.Session.IsAuthenticated);
        Assert.Equal(liveBefore, await HashFileAsync(fixture.LivePath));
        Assert.Equal(filesBefore, Directory.GetFiles(fixture.Root, "*", SearchOption.AllDirectories).Order().ToArray());
    }

    [Theory]
    [InlineData(".db")]
    [InlineData(".sqlite")]
    [InlineData(".db-wal")]
    [InlineData(".zip")]
    [InlineData(".fmbak")]
    public async Task Restore_RejectsLooseSqliteEvenWhenRenamed(string extension)
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var database = Path.Combine(fixture.Root, "loose" + extension);
        await CreateApplicationDatabaseAsync(database, 3, activeResponsible: true);
        var hashBefore = await HashFileAsync(database);
        var result = await fixture.Service.RestoreAsync(new(database));
        Assert.False(result.Success);
        Assert.Equal(DatabaseBackupFailureReason.InvalidBackup, result.FailureReason);
        Assert.Null(result.PreRestoreBackupPath);
        Assert.Equal(hashBefore, await HashFileAsync(database));
    }

    [Fact]
    public async Task Restore_RejectsArtifactWithValidChecksumButBrokenForeignKey()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();
        var database = Path.Combine(fixture.Root, "broken-fk.db");
        await CreateApplicationDatabaseAsync(database, 3, activeResponsible: true);
        await ExecuteAsync(database, "PRAGMA foreign_keys=OFF; INSERT INTO UserPermissions(UserId,Module,CanView,CanCreate,CanModify) VALUES (999,'Sales',1,0,0);");
        await AssertRestoreRejectedAndLiveIntactAsync(fixture, await PackDatabaseAsync(database), DatabaseBackupFailureReason.InvalidBackup);
    }

    [Fact]
    public async Task Backup_UnreadableLiveDatabasePublishesNoArtifactAndCleansWorkspace()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        await fixture.Context.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();
        await File.WriteAllTextAsync(fixture.LivePath, "not a SQLite database");
        var result = await fixture.Service.CreateLocalBackupAsync();
        Assert.False(result.Success);
        Assert.Empty(Directory.GetFileSystemEntries(fixture.BackupDirectory));
    }

    [Fact]
    public async Task Restore_AcceptsBoundedZip64Envelope()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var backup = await fixture.Service.CreateLocalBackupAsync();
        Assert.True(backup.Success);
        var bytes = await File.ReadAllBytesAsync(backup.BackupPath!);
        var endOffset = bytes.Length - 22;
        var directorySize = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(endOffset + 12));
        var directoryOffset = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(endOffset + 16));
        using var stream = new MemoryStream();
        stream.Write(bytes, 0, endOffset);
        using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(0x06064b50u); writer.Write(44UL);
            writer.Write((ushort)45); writer.Write((ushort)45);
            writer.Write(0u); writer.Write(0u);
            writer.Write(2UL); writer.Write(2UL);
            writer.Write((ulong)directorySize); writer.Write((ulong)directoryOffset);
            writer.Write(0x07064b50u); writer.Write(0u);
            writer.Write((ulong)endOffset); writer.Write(1u);
        }
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(endOffset + 16), uint.MaxValue);
        stream.Write(bytes, endOffset, 22);
        var path = Path.Combine(fixture.Root, "zip64.fmbak");
        await File.WriteAllBytesAsync(path, stream.ToArray());
        var result = await fixture.Service.RestoreAsync(new(path));
        Assert.True(result.Success);
        Assert.Equal("ok", await ScalarStringAsync(fixture.LivePath, "PRAGMA integrity_check;"));
    }

    private static async Task AssertRestoreRejectedAndLiveIntactAsync(
        BackupFixture fixture,
        string sourcePath,
        DatabaseBackupFailureReason expectedFailure)
    {
        if (Path.GetExtension(sourcePath) != ".fmbak") sourcePath = await PackDatabaseAsync(sourcePath);
        var sourceHash = await HashFileAsync(sourcePath);
        var result = await fixture.Service.RestoreAsync(new(sourcePath));
        Assert.False(result.Success);
        Assert.Equal(expectedFailure, result.FailureReason);
        Assert.Equal(sourceHash, await HashFileAsync(sourcePath));
        Assert.Null(result.PreRestoreBackupPath);
        Assert.False(result.RestartRequired);
        Assert.Empty(Directory.GetFiles(fixture.Root, "FinancialManagement_Restore*"));
        await fixture.AssertLiveMarkerAsync();
    }

    private static byte[] ManifestFor(byte[] database) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        BackupFormat = "FinancialManagementBackup", BackupFormatVersion = 1, CreatedAtUtc = TestUtc,
        DatabaseFileName = "database.sqlite", DatabaseLength = database.LongLength,
        DatabaseSha256 = Convert.ToHexString(SHA256.HashData(database))
    });

    private static async Task WriteArchiveAsync(string path, IEnumerable<(string Name, byte[] Bytes)> entries,
        CompressionLevel compression = CompressionLevel.NoCompression)
    {
        await using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (var (name, bytes) in entries)
        {
            using var entry = archive.CreateEntry(name, compression).Open();
            await entry.WriteAsync(bytes);
        }
    }

    private static async Task<string> PackDatabaseAsync(string databasePath)
    {
        var database = await File.ReadAllBytesAsync(databasePath);
        var artifact = databasePath + ".fmbak";
        await WriteArchiveAsync(artifact, [("database.sqlite", database), ("manifest.json", ManifestFor(database))]);
        return artifact;
    }

    private static async Task<string> ExtractForInspectionAsync(string artifactPath)
    {
        using var archive = ZipFile.OpenRead(artifactPath);
        Assert.Equal(new[] { "database.sqlite", "manifest.json" }, archive.Entries.Select(entry => entry.FullName).Order().ToArray());
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetDirectoryName(artifactPath)!, "inspection-" + Guid.NewGuid().ToString("N"))).FullName;
        var databasePath = Path.Combine(directory, "database.sqlite");
        await using (var database = File.Create(databasePath))
        using (var entry = archive.GetEntry("database.sqlite")!.Open())
            await entry.CopyToAsync(database);
        using var manifestStream = archive.GetEntry("manifest.json")!.Open();
        using var manifest = await JsonDocument.ParseAsync(manifestStream);
        Assert.Equal(new FileInfo(databasePath).Length, manifest.RootElement.GetProperty("DatabaseLength").GetInt64());
        Assert.Equal(Convert.ToHexString(await HashFileAsync(databasePath)), manifest.RootElement.GetProperty("DatabaseSha256").GetString());
        return databasePath;
    }

    private static async Task<byte[]> HashFileAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return await SHA256.HashDataAsync(stream);
    }

    private static async Task CreateApplicationDatabaseAsync(
        string path,
        int migrationCount,
        bool activeResponsible,
        string? clientName = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure($"Data Source={path};Pooling=False");
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var context = scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        var migrations = context.Database.GetMigrations().ToArray();
        await context.GetService<IMigrator>().MigrateAsync(migrations[migrationCount - 1]);
        context.Users.Add(new User
        {
            Id = 1,
            FullName = "Backup owner",
            Username = "backup-owner",
            PasswordHash = "unused",
            Role = UserRole.Responsable,
            IsActive = activeResponsible,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        });
        if (clientName is not null)
        {
            context.Clients.Add(NewClient(clientName));
        }

        await context.SaveChangesAsync();
        await context.Database.CloseConnectionAsync();
        SqliteConnection.ClearAllPools();
    }

    private static async Task ExecuteAsync(string databasePath, string sql)
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false,
                ForeignKeys = true
            }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static void ExecuteSynchronously(string databasePath, string sql)
    {
        using var connection = NewConnection(databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static long ScalarLongSynchronously(string databasePath, string sql)
    {
        using var connection = NewConnection(databasePath);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    private static async Task<long> ScalarLongAsync(string databasePath, string sql)
    {
        await using var connection = NewConnection(databasePath);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task<string?> ScalarStringAsync(string databasePath, string sql)
    {
        await using var connection = NewConnection(databasePath);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToString(await command.ExecuteScalarAsync());
    }

    private static SqliteConnection NewConnection(string databasePath) => new(
        new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWrite,
            Pooling = false,
            ForeignKeys = true
        }.ToString());

    private sealed class BackupFixture : IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly AsyncServiceScope scope;

        private BackupFixture(string root, ServiceProvider provider, AsyncServiceScope scope)
        {
            Root = root;
            LivePath = Path.Combine(root, "live.db");
            BackupDirectory = Path.Combine(root, "backups");
            this.provider = provider;
            this.scope = scope;
            Context = scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
            Service = scope.ServiceProvider.GetRequiredService<IDatabaseBackupService>();
            Session = scope.ServiceProvider.GetRequiredService<ICurrentUserSession>();
            RestoreTestHook = scope.ServiceProvider.GetRequiredService<DatabaseRestoreTestHook>();
        }

        public string Root { get; }
        public string LivePath { get; }
        public string BackupDirectory { get; }
        public FinancialManagementDbContext Context { get; }
        public IDatabaseBackupService Service { get; }
        public ICurrentUserSession Session { get; }
        public DatabaseRestoreTestHook RestoreTestHook { get; }
        public DatabaseMaintenanceLock MaintenanceLock => scope.ServiceProvider.GetRequiredService<DatabaseMaintenanceLock>();
        public IServiceProvider Services => scope.ServiceProvider;

        public static async Task<BackupFixture> CreateAsync(bool authenticate = false)
        {
            var root = Path.Combine(Path.GetTempPath(), $"FinancialManagementBackupTests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            var livePath = Path.Combine(root, "live.db");
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestUtc));
            services.AddSingleton(new DatabaseBackupOptions(Path.Combine(root, "backups")));
            services.AddInfrastructure($"Data Source={livePath};Pooling=False");
            var provider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });
            var scope = provider.CreateAsyncScope();
            var fixture = new BackupFixture(root, provider, scope);
            await fixture.Context.Database.MigrateAsync();
            await fixture.AddUserAsync("responsable", UserRole.Responsable, active: true);
            if (authenticate)
            {
                await fixture.AuthenticateAsync("responsable", "Password-123!");
            }

            return fixture;
        }

        public async Task AuthenticateAsync(string username, string password)
        {
            var authentication = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
            var result = await authentication.AuthenticateAsync(username, password);
            Assert.True(result.Success);
        }

        public async Task AddEmployeeWithEveryPermissionAsync()
        {
            var employee = await AddUserAsync("employee", UserRole.Employe, active: true);
            Context.UserPermissions.AddRange(Enum.GetValues<PermissionModule>().Select(module => new UserPermission
            {
                UserId = employee.Id,
                Module = module,
                CanView = true,
                CanCreate = true,
                CanModify = true
            }));
            await Context.SaveChangesAsync();
        }

        public async Task AddRepresentativeDataAsync(string suffix)
        {
            var userId = await Context.Users
                .Where(user => user.Username == "responsable")
                .Select(user => user.Id)
                .SingleAsync();
            var client = new Client
            {
                Name = $"Client {suffix}",
                Phone = "0600000000",
                CreatedAtUtc = TestUtc,
                UpdatedAtUtc = TestUtc
            };
            var supplier = new Supplier
            {
                Name = $"Supplier {suffix}",
                CreatedAtUtc = TestUtc,
                UpdatedAtUtc = TestUtc
            };
            Context.AddRange(client, supplier);
            await Context.SaveChangesAsync();

            Context.AddRange(
                new Sale
                {
                    Reference = $"SALE-{suffix}",
                    SaleDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    TotalAmountMinor = 125_075,
                    ClientId = client.Id,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc,
                    UpdatedAtUtc = TestUtc
                },
                new CustomerPayment
                {
                    Reference = $"CP-{suffix}",
                    ClientId = client.Id,
                    PaymentDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    AmountMinor = 25_000,
                    PaymentMethod = PaymentMethod.Cash,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc
                },
                new Purchase
                {
                    Reference = $"PURCHASE-{suffix}",
                    PurchaseDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    TotalAmountMinor = 80_000,
                    SupplierId = supplier.Id,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc,
                    UpdatedAtUtc = TestUtc
                },
                new SupplierPayment
                {
                    Reference = $"SP-{suffix}",
                    SupplierId = supplier.Id,
                    PaymentDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    AmountMinor = 15_000,
                    PaymentMethod = PaymentMethod.BankTransfer,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc
                },
                new Expense
                {
                    Reference = $"EXP-{suffix}",
                    ExpenseDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    ExpenseCategoryId = 1,
                    AmountMinor = 5_000,
                    PaymentMethod = PaymentMethod.Cash,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc,
                    UpdatedAtUtc = TestUtc
                },
                new TreasuryOpeningBalance
                {
                    OpeningDate = DateOnly.FromDateTime(TestUtc.UtcDateTime),
                    CashAmountMinor = 100_000,
                    BankAmountMinor = 200_000,
                    CreatedByUserId = userId,
                    CreatedAtUtc = TestUtc
                });
            await Context.SaveChangesAsync();
        }

        public FinancialManagementDbContext CreateIndependentContext(string databasePath)
        {
            var options = new DbContextOptionsBuilder<FinancialManagementDbContext>()
                .UseSqlite($"Data Source={databasePath};Pooling=False")
                .Options;
            return new FinancialManagementDbContext(options);
        }

        public async Task AssertLiveMarkerAsync()
        {
            await using var verification = CreateIndependentContext(LivePath);
            Assert.Equal(1, await verification.Clients.CountAsync(client => client.Name == "Live marker"));
        }

        private async Task<User> AddUserAsync(string username, UserRole role, bool active)
        {
            var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
            var user = new User
            {
                FullName = username,
                Username = username,
                PasswordHash = hasher.HashPassword("Password-123!"),
                Role = role,
                IsActive = active,
                CreatedAtUtc = TestUtc,
                UpdatedAtUtc = TestUtc
            };
            Context.Users.Add(user);
            await Context.SaveChangesAsync();
            return user;
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
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

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
