using System.Data.Common;
using FinancialManagement.Application.Backup;
using FinancialManagement.Infrastructure;
using FinancialManagement.Infrastructure.Backup;
using FinancialManagement.Infrastructure.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace FinancialManagement.Tests;

public sealed class FinancialStorageIntegrityTests
{
    public static TheoryData<string, string> Columns => new()
    {
        { "Sales", "TotalAmountMinor" }, { "Purchases", "TotalAmountMinor" }, { "Expenses", "AmountMinor" },
        { "CustomerPayments", "AmountMinor" }, { "CustomerPaymentAllocations", "AmountMinor" },
        { "InitialCustomerReceivables", "InitialAmountMinor" }, { "SupplierPayments", "AmountMinor" },
        { "SupplierPaymentAllocations", "AmountMinor" }, { "InitialSupplierPayables", "InitialAmountMinor" },
        { "TreasuryOpeningBalance", "CashAmountMinor" }, { "TreasuryOpeningBalance", "BankAmountMinor" },
        { "TreasuryTransfers", "AmountMinor" }
    };

    [Theory]
    [MemberData(nameof(Columns))]
    public async Task All_columns_enforce_storage_accept_lossless_inputs_and_preserve_business_bounds(string table, string column)
    {
        await using var db = new FinancialStorageTestDatabase();
        await db.Context.Database.MigrateAsync();
        await FinancialStorageTestDatabase.SeedAsync(db.Context);
        foreach (var value in new object[] { 100L, 100.0d, "100", "1e2", long.MaxValue })
        {
            await db.UpdateAsync(table, column, value);
            Assert.Equal(["integer|" + (value is long l ? l.ToString() : "100")],
                await db.QueryAsync($"SELECT typeof(\"{column}\"),quote(\"{column}\") FROM \"{table}\" WHERE Id=1"));
        }
        foreach (var value in new object[] { 100.5d, "100.5", "abc", new byte[] { 1, 2 }, DBNull.Value })
        {
            var before = await db.ValuesAsync();
            var error = await Assert.ThrowsAsync<SqliteException>(() => db.UpdateAsync(table, column, value));
            Assert.Equal(19, error.SqliteErrorCode);
            Assert.Equal(before, await db.ValuesAsync());
        }
        if (table == "TreasuryOpeningBalance")
        {
            await db.UpdateAsync(table, column, long.MinValue);
            Assert.Equal(["integer|-9223372036854775808"],
                await db.QueryAsync($"SELECT typeof(\"{column}\"),quote(\"{column}\") FROM \"{table}\" WHERE Id=1"));
        }
        else
            Assert.Contains("_Positive", (await Assert.ThrowsAsync<SqliteException>(() => db.UpdateAsync(table, column, long.MinValue))).Message);
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public async Task Raw_insert_checks_cover_every_column_as_well_as_updates(string table, string column)
    {
        await using var db = new FinancialStorageTestDatabase();
        await db.Context.Database.MigrateAsync();
        await FinancialStorageTestDatabase.SeedAsync(db.Context);
        var before = await db.ValuesAsync();
        foreach (var value in new object[] { 100L, 100.0d, "100", "1e2" })
            Assert.Equal("integer|100", await InsertAsync(value));
        foreach (var value in new object[] { 100.5d, "100.5", "abc" })
            Assert.Contains("_IntegerStorage", (await Assert.ThrowsAsync<SqliteException>(() => InsertAsync(value))).Message);
        Assert.Equal(before, await db.ValuesAsync());

        async Task<string> InsertAsync(object value)
        {
            await using var c = new SqliteConnection($"Data Source={db.Path};Foreign Keys=True;Pooling=False");
            await c.OpenAsync();
            await using var tx = c.BeginTransaction();
            var fields = new Dictionary<string, object>();
            await using (var read = c.CreateCommand())
            {
                read.Transaction = tx;
                read.CommandText = $"SELECT * FROM \"{table}\" WHERE Id=1";
                await using var reader = await read.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                for (var i = 0; i < reader.FieldCount; i++)
                    if (reader.GetName(i) != "Id") fields.Add(reader.GetName(i), reader.GetValue(i));
            }
            // Test isolation only: the existing singleton is restored by rollback.
            if (table == "TreasuryOpeningBalance")
            {
                await using var remove = c.CreateCommand();
                remove.Transaction = tx;
                remove.CommandText = "DELETE FROM TreasuryOpeningBalance";
                await remove.ExecuteNonQueryAsync();
            }
            fields[column] = value;
            if (fields.ContainsKey("Reference")) fields["Reference"] = Guid.NewGuid().ToString("N");
            await using var insert = c.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = $"INSERT INTO \"{table}\" ({string.Join(",", fields.Keys.Select(k => "\"" + k + "\""))}) VALUES ({string.Join(",", fields.Keys.Select((_, i) => "$p" + i))});";
            var position = 0;
            foreach (var field in fields.Values) insert.Parameters.AddWithValue("$p" + position++, field);
            await insert.ExecuteNonQueryAsync();
            await using var check = c.CreateCommand();
            check.Transaction = tx;
            check.CommandText = $"SELECT typeof(\"{column}\") || '|' || quote(\"{column}\") FROM \"{table}\" WHERE Id=last_insert_rowid()";
            var stored = (string)(await check.ExecuteScalarAsync())!;
            await tx.RollbackAsync();
            return stored;
        }
    }

    [Fact]
    public async Task Valid_legacy_up_down_preserves_all_values_metadata_checks_and_singleton()
    {
        await using var db = new FinancialStorageTestDatabase();
        await db.LegacyAsync();
        // Test high-water preservation even where the highest generated row was deleted.
        await db.Context.Database.ExecuteSqlRawAsync("UPDATE sqlite_sequence SET seq=12345 WHERE name='Sales'");
        var values = await db.ValuesAsync();
        var metadata = await db.MetadataAsync();
        foreach (var target in new[] { db.Migrations[3], db.Migrations[2] })
        {
            await db.Context.GetService<IMigrator>().MigrateAsync(target);
            Assert.Equal(values, await db.ValuesAsync());
            Assert.Equal(metadata, await db.MetadataAsync());
            Assert.Equal(["12345"], await db.QueryAsync("SELECT seq FROM sqlite_sequence WHERE name='Sales'"));
            Assert.Equal(["ok"], await db.QueryAsync("PRAGMA integrity_check"));
            Assert.Empty(await db.QueryAsync("PRAGMA foreign_key_check"));
            Assert.Equal(["1"], await db.QueryAsync("PRAGMA foreign_keys"));
            await using var c = new SqliteConnection($"Data Source={db.Path};Foreign Keys=True;Pooling=False");
            await c.OpenAsync();
            Assert.True(await new DatabaseSchemaValidator(db.Context).ValidateAsync(c,
                (await db.Context.Database.GetAppliedMigrationsAsync()).ToArray(), CancellationToken.None));
            Assert.True(await FinancialStorageValidator.ValidateAsync(c, false));
            var error = await Assert.ThrowsAsync<SqliteException>(() => db.Context.Database.ExecuteSqlRawAsync("""
                INSERT INTO TreasuryOpeningBalance(OpeningDate,CashAmountMinor,BankAmountMinor,CreatedByUserId,CreatedAtUtc)
                VALUES('2026-01-02',1,1,1,'2026-01-02');
                """));
            Assert.Contains("UX_TreasuryOpeningBalance_Singleton", error.Message);
        }
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public async Task Legacy_REAL_and_TEXT_fail_before_copy_with_original_storage_and_schema_unchanged(string table, string column)
    {
        foreach (var value in new object[] { 100.5d, "abc" })
        {
            await using var db = new FinancialStorageTestDatabase();
            await db.LegacyAsync();
            await db.UpdateAsync(table, column, value);
            var values = await db.ValuesAsync();
            var schema = await db.QueryAsync("SELECT name,sql FROM sqlite_master WHERE name NOT LIKE '__EF%' ORDER BY name");
            var error = await Assert.ThrowsAsync<SqliteException>(() => db.Context.Database.MigrateAsync());
            Assert.Contains("CK_Astra005Guard_Integer", error.Message);
            Assert.Equal(values, await db.ValuesAsync());
            Assert.Equal(schema, await db.QueryAsync("SELECT name,sql FROM sqlite_master WHERE name NOT LIKE '__EF%' ORDER BY name"));
            Assert.Equal(db.Migrations.Take(3), await db.Context.Database.GetAppliedMigrationsAsync());
        }
    }

    [Theory]
    [InlineData(false, "sql")]
    [InlineData(true, "sql")]
    [InlineData(false, "storage")]
    [InlineData(true, "storage")]
    [InlineData(false, "fk")]
    [InlineData(true, "fk")]
    public async Task Failure_after_replacement_rolls_back_values_schema_indexes_and_history(bool down, string fault)
    {
        var interceptor = new FailDuringRebuild { Fault = fault };
        await using var db = new FinancialStorageTestDatabase(interceptor);
        await db.LegacyAsync();
        if (down) await db.Context.Database.MigrateAsync();
        var values = await db.ValuesAsync();
        var schema = await db.QueryAsync("SELECT name,sql FROM sqlite_master WHERE name NOT LIKE '__EF%' ORDER BY name");
        var history = (await db.Context.Database.GetAppliedMigrationsAsync()).ToArray();
        interceptor.Armed = true;
        var failure = await Assert.ThrowsAsync<SqliteException>(() => db.Context.GetService<IMigrator>().MigrateAsync(down ? db.Migrations[2] : db.Migrations[3]));
        if (fault != "sql") Assert.Contains("CK_Astra005Guard_Integer", failure.Message);
        Assert.True(interceptor.Injected);
        Assert.Equal(values, await db.ValuesAsync());
        Assert.Equal(schema, await db.QueryAsync("SELECT name,sql FROM sqlite_master WHERE name NOT LIKE '__EF%' ORDER BY name"));
        Assert.Equal(history, await db.Context.Database.GetAppliedMigrationsAsync());
        Assert.Equal(["ok"], await db.QueryAsync("PRAGMA integrity_check"));
        Assert.Empty(await db.QueryAsync("PRAGMA foreign_key_check"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_migrates_fresh_and_valid_legacy_before_returning(bool legacy)
    {
        await using var db = new FinancialStorageTestDatabase();
        if (legacy) await db.LegacyAsync();
        var before = legacy ? await db.ValuesAsync() : [];
        await FinancialDatabaseStartup.InitializeAsync(
            db.Context,
            new StorageStartupBackupService(db.Path));
        Assert.Equal(db.Migrations, await db.Context.Database.GetAppliedMigrationsAsync());
        if (legacy) Assert.Equal(before, await db.ValuesAsync());
        else
        {
            await FinancialStorageTestDatabase.SeedAsync(db.Context);
            await using var c = new SqliteConnection($"Data Source={db.Path};Pooling=False");
            await c.OpenAsync();
            Assert.True(await FinancialStorageValidator.ValidateAsync(c, false));
        }
    }

    [Theory]
    [MemberData(nameof(Columns))]
    public async Task Startup_rejects_legacy_and_current_corruption_before_normal_flow(string table, string column)
    {
        foreach (var hardened in new[] { false, true })
        foreach (var value in new object[] { 100.5d, "abc" })
        {
            await using var db = new FinancialStorageTestDatabase();
            await db.LegacyAsync();
            if (hardened) await db.Context.Database.MigrateAsync();
            await db.UpdateAsync(table, column, value, bypassChecks: hardened);
            var before = await db.ValuesAsync();
            var history = (await db.Context.Database.GetAppliedMigrationsAsync()).ToArray();
            await Assert.ThrowsAsync<FinancialStorageIntegrityException>(() =>
                FinancialDatabaseStartup.InitializeAsync(
                    db.Context,
                    new StorageStartupBackupService(db.Path)));
            Assert.Equal(before, await db.ValuesAsync());
            Assert.Equal(history, await db.Context.Database.GetAppliedMigrationsAsync());
        }
    }

    [Theory]
    [InlineData("DROP TABLE TreasuryTransfers")]
    [InlineData("ALTER TABLE TreasuryOpeningBalance DROP COLUMN CashAmountMinor")]
    public async Task Startup_does_not_hide_missing_legacy_tables_or_columns(string fault)
    {
        await using var db = new FinancialStorageTestDatabase();
        await db.Context.GetService<IMigrator>().MigrateAsync(db.Migrations[2]);
        await db.Context.Database.ExecuteSqlRawAsync(fault);
        await Assert.ThrowsAsync<FinancialStorageIntegrityException>(() =>
            FinancialDatabaseStartup.InitializeAsync(
                db.Context,
                new StorageStartupBackupService(db.Path)));
    }

    [Fact]
    public void Up_down_scripts_have_one_transaction_and_history_before_commit()
    {
        using var db = new FinancialManagementDbContext(new DbContextOptionsBuilder<FinancialManagementDbContext>().UseSqlite("Data Source=:memory:").Options);
        var migrations = db.Database.GetMigrations().ToArray();
        var migration = new _004_EnforceFinancialIntegerStorageClass();
        foreach (var operation in migration.UpOperations.Concat(migration.DownOperations))
            Assert.False(Assert.IsType<SqlOperation>(operation).SuppressTransaction);
        foreach (var (from, to) in new[] { (migrations[2], migrations[3]), (migrations[3], migrations[2]) })
        {
            var script = db.GetService<IMigrator>().GenerateScript(from, to);
            Assert.Equal(1, script.Split("BEGIN TRANSACTION;").Length - 1);
            Assert.Equal(1, script.Split("COMMIT;").Length - 1);
            Assert.True(script.LastIndexOf("__EFMigrationsHistory", StringComparison.Ordinal) < script.IndexOf("COMMIT;", StringComparison.Ordinal));
            Assert.Contains("CREATE UNIQUE INDEX \"UX_TreasuryOpeningBalance_Singleton\"", script);
            Assert.DoesNotContain("PRAGMA foreign_keys", script);
            Assert.DoesNotContain("CAST(", script);
        }
    }

    [Fact]
    public void Schema_validator_accepts_only_fingerprinted_non_suppressed_004_SQL()
    {
        Assert.True(DatabaseSchemaValidator.HasApprovedStorageMigrationSql(new _004_EnforceFinancialIntegerStorageClass()));
        var changed = new _004_EnforceFinancialIntegerStorageClass();
        ((SqlOperation)changed.UpOperations.Single()).Sql += "\nCREATE TABLE Unexpected(Id INTEGER);";
        Assert.False(DatabaseSchemaValidator.HasApprovedStorageMigrationSql(changed));
        var suppressed = new _004_EnforceFinancialIntegerStorageClass();
        ((SqlOperation)suppressed.UpOperations.Single()).SuppressTransaction = true;
        Assert.False(DatabaseSchemaValidator.HasApprovedStorageMigrationSql(suppressed));
        Assert.False(DatabaseSchemaValidator.HasApprovedStorageMigrationSql(new UnexpectedRawMigration()));
    }

    private sealed class UnexpectedRawMigration : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("SELECT 1;");
    }

    private sealed class StorageStartupBackupService(string databasePath) : IStartupDatabaseBackupService
    {
        private static readonly DateOnly TestDate = new(2026, 1, 1);

        public Task<StartupDatabaseBackupResult> CreatePreMigrationBackupAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var backupPath = databasePath + ".fmbak";
            File.WriteAllText(backupPath, "Financial storage startup test artifact");
            return Task.FromResult(StartupDatabaseBackupResult.Succeeded(backupPath, TestDate));
        }

        public Task<StartupDatabaseBackupResult> CreateDailyBackupIfNeededAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(StartupDatabaseBackupResult.Failed(
                DatabaseBackupFailureReason.StorageFailure,
                TestDate));
        }
    }

    private sealed class FailDuringRebuild : DbCommandInterceptor
    {
        public string Fault { get; init; } = "sql";
        public bool Armed { get; set; }
        public bool Injected { get; private set; }
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed && command.CommandText.Contains("CREATE TEMP TABLE \"__Astra005Guard\""))
            {
                // Executes after copies, drops, renames and index recreation,
                // while EF still owns the same transaction. No production hook.
                var injectedSql = Fault switch
                {
                    "storage" => "PRAGMA ignore_check_constraints=ON; UPDATE Sales SET TotalAmountMinor=100.5; PRAGMA ignore_check_constraints=OFF;",
                    "fk" => "UPDATE CustomerPaymentAllocations SET CustomerPaymentId=999999;",
                    _ => "SELECT * FROM __Astra005DeliberateFailure;"
                };
                command.CommandText = command.CommandText.Replace("DROP TABLE \"__Astra005Sequence\";",
                    injectedSql + " DROP TABLE \"__Astra005Sequence\";");
                Injected = true;
            }
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }
    }
}
