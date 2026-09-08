using FinancialManagement.Application.Backup;
using FinancialManagement.Infrastructure;
using FinancialManagement.Infrastructure.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Tests;

public sealed partial class DatabaseBackupServiceTests
{
    [Theory]
    [MemberData(nameof(FinancialStorageIntegrityTests.Columns), MemberType = typeof(FinancialStorageIntegrityTests))]
    public async Task Restore_rejects_all_legacy_REAL_and_TEXT_columns_before_swap(string table, string column)
    {
        foreach (var value in new object[] { 100.5d, "abc" })
        {
            await using var source = new FinancialStorageTestDatabase();
            await source.LegacyAsync();
            await source.UpdateAsync(table, column, value);
            await using var destination = await BackupFixture.CreateAsync(authenticate: true);
            destination.Context.Clients.Add(NewClient("Live marker"));
            await destination.Context.SaveChangesAsync();
            var artifact = await PackDatabaseAsync(source.Path);
            var sourceBefore = await source.ValuesAsync();
            var artifactBefore = await HashFileAsync(artifact);
            var liveBefore = await HashFileAsync(destination.LivePath);
            var swapped = false;
            destination.RestoreTestHook.AfterAtomicReplace = _ => swapped = true;
            var result = await destination.Service.RestoreAsync(new(artifact));
            Assert.Equal(DatabaseBackupFailureReason.InvalidBackup, result.FailureReason);
            Assert.False(result.Success);
            Assert.False(swapped);
            Assert.Null(result.PreRestoreBackupPath);
            Assert.Equal(sourceBefore, await source.ValuesAsync());
            Assert.Equal(artifactBefore, await HashFileAsync(artifact));
            Assert.Equal(liveBefore, await HashFileAsync(destination.LivePath));
            await destination.AssertLiveMarkerAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Restore_preserves_all_financial_values_from_legacy_and_hardened_backups(bool hardened)
    {
        await using var source = new FinancialStorageTestDatabase();
        await source.LegacyAsync();
        if (hardened) await source.Context.Database.MigrateAsync();
        var values = await source.ValuesAsync();
        await using var destination = await BackupFixture.CreateAsync(authenticate: true);
        var artifact = await PackDatabaseAsync(source.Path);
        var result = await destination.Service.RestoreAsync(new(artifact));
        Assert.True(result.Success);
        Assert.Equal(4L, await ScalarLongAsync(destination.LivePath, "SELECT COUNT(*) FROM __EFMigrationsHistory"));
        foreach (var pair in FinancialStorageIntegrityTests.Columns)
        {
            var table = (string)pair[0];
            var column = (string)pair[1];
            Assert.Equal(0L, await ScalarLongAsync(destination.LivePath,
                $"SELECT COUNT(*) FROM \"{table}\" WHERE typeof(\"{column}\")<>'integer'"));
        }
        Assert.Equal(values, await source.ValuesAsync());
        // Reuse the canonical data snapshot routine against the restored database.
        await using var comparison = new FinancialStorageTestDatabase();
        File.Copy(destination.LivePath, comparison.Path, overwrite: true);
        Assert.Equal(values, await comparison.ValuesAsync());
    }

    [Fact]
    public async Task Hardened_backup_creation_and_restore_preserve_all_amounts()
    {
        await using var source = await BackupFixture.CreateAsync(authenticate: true);
        await FinancialStorageTestDatabase.SeedAsync(source.Context);
        await using var destination = await BackupFixture.CreateAsync(authenticate: true);
        var backup = await source.Service.CreateLocalBackupAsync();
        Assert.True(backup.Success);
        Assert.True((await destination.Service.RestoreAsync(new(backup.BackupPath!))).Success);
        await using var connection = NewConnection(destination.LivePath);
        await connection.OpenAsync();
        Assert.True(await FinancialStorageValidator.ValidateAsync(connection, false));
        foreach (var pair in FinancialStorageIntegrityTests.Columns)
        {
            var table = (string)pair[0];
            var column = (string)pair[1];
            Assert.Equal(await ScalarStringAsync(source.LivePath, $"SELECT group_concat(quote(\"{column}\"),'|') FROM \"{table}\" ORDER BY Id"),
                await ScalarStringAsync(destination.LivePath, $"SELECT group_concat(quote(\"{column}\"),'|') FROM \"{table}\" ORDER BY Id"));
        }
    }

    [Fact]
    public async Task Backup_rejects_bypassed_storage_corruption_without_repairing_source()
    {
        await using var source = await BackupFixture.CreateAsync(authenticate: true);
        await FinancialStorageTestDatabase.SeedAsync(source.Context);
        await ExecuteAsync(source.LivePath, "PRAGMA ignore_check_constraints=ON; UPDATE Sales SET TotalAmountMinor=100.5;");
        var before = await HashFileAsync(source.LivePath);
        var result = await source.Service.CreateLocalBackupAsync();
        Assert.False(result.Success);
        Assert.Equal(DatabaseBackupFailureReason.InvalidBackup, result.FailureReason);
        Assert.Equal(before, await HashFileAsync(source.LivePath));
        Assert.Equal("real", await ScalarStringAsync(source.LivePath, "SELECT typeof(TotalAmountMinor) FROM Sales"));
    }

    [Theory]
    [InlineData("singleton")]
    [InlineData("storage-check")]
    public async Task Current_schema_validation_still_rejects_missing_singleton_or_weakened_storage_check(string fault)
    {
        await using var source = new FinancialStorageTestDatabase();
        await source.LegacyAsync();
        await source.Context.Database.MigrateAsync();
        if (fault == "singleton")
            await ExecuteAsync(source.Path, "DROP INDEX UX_TreasuryOpeningBalance_Singleton;");
        else
            await RewriteSchemaAsync(source.Path, "Sales", "typeof(\"TotalAmountMinor\") = 'integer'", "1");
        await using var destination = await BackupFixture.CreateAsync(authenticate: true);
        var swapped = false;
        destination.RestoreTestHook.AfterAtomicReplace = _ => swapped = true;
        var result = await destination.Service.RestoreAsync(new(await PackDatabaseAsync(source.Path)));
        Assert.Equal(DatabaseBackupFailureReason.InvalidBackup, result.FailureReason);
        Assert.False(swapped);
    }
}
