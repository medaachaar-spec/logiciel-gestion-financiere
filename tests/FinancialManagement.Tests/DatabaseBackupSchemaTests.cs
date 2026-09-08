using FinancialManagement.Application.Backup;
using FinancialManagement.Infrastructure.Backup;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace FinancialManagement.Tests;

public sealed partial class DatabaseBackupServiceTests
{
    [Theory]
    [InlineData("missing-table")]
    [InlineData("missing-column")]
    [InlineData("fake-singleton")]
    [InlineData("unknown-migration")]
    [InlineData("history-ahead")]
    [InlineData("history-behind")]
    [InlineData("migration-gap")]
    [InlineData("missing-first-migration")]
    [InlineData("history-column")]
    [InlineData("history-pk")]
    [InlineData("declared-type")]
    [InlineData("nullability")]
    [InlineData("primary-key")]
    [InlineData("missing-fk")]
    [InlineData("fk-delete")]
    [InlineData("missing-index")]
    [InlineData("nonunique-index")]
    [InlineData("wrong-index-key")]
    [InlineData("index-order")]
    [InlineData("index-collation")]
    [InlineData("partial-index")]
    [InlineData("missing-singleton")]
    [InlineData("nonunique-singleton")]
    [InlineData("wrong-singleton-key")]
    [InlineData("partial-singleton")]
    [InlineData("singleton-null-expression")]
    [InlineData("singleton-descending")]
    [InlineData("check-weakened")]
    [InlineData("xor-weakened")]
    [InlineData("extra-table")]
    [InlineData("extra-column")]
    [InlineData("extra-trigger")]
    public async Task Restore_RejectsForgedSchemaBeforeSwap(string fault)
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        fixture.Context.Clients.Add(NewClient("Live marker"));
        await fixture.Context.SaveChangesAsync();
        var database = Path.Combine(fixture.Root, "forged.db");
        await CreateApplicationDatabaseAsync(database, fault == "history-ahead" ? 1 : 3, activeResponsible: true);
        var migrations = fixture.Context.Database.GetMigrations().ToArray();
        var sql = fault switch
        {
            "missing-table" => "DROP TABLE ApplicationSettings;",
            "missing-column" => "ALTER TABLE Clients DROP COLUMN Observation;",
            "fake-singleton" => "DROP INDEX UX_TreasuryOpeningBalance_Singleton; CREATE INDEX UX_TreasuryOpeningBalance_Singleton ON TreasuryOpeningBalance(OpeningDate);",
            "unknown-migration" => "INSERT INTO __EFMigrationsHistory VALUES ('99999999999999_004_Unknown','10.0.11');",
            "history-ahead" => $"INSERT INTO __EFMigrationsHistory VALUES ('{migrations[1]}','10.0.11');",
            "history-behind" => $"DELETE FROM __EFMigrationsHistory WHERE MigrationId <> '{migrations[0]}';",
            "migration-gap" => $"DELETE FROM __EFMigrationsHistory WHERE MigrationId = '{migrations[1]}';",
            "missing-first-migration" => $"DELETE FROM __EFMigrationsHistory WHERE MigrationId = '{migrations[0]}';",
            "history-column" => "ALTER TABLE __EFMigrationsHistory RENAME COLUMN ProductVersion TO FakeVersion;",
            "history-pk" => "ALTER TABLE __EFMigrationsHistory RENAME TO OldHistory; CREATE TABLE __EFMigrationsHistory(MigrationId TEXT NOT NULL, ProductVersion TEXT NOT NULL); INSERT INTO __EFMigrationsHistory SELECT * FROM OldHistory; DROP TABLE OldHistory;",
            "primary-key" => "DROP TABLE ApplicationSettings; CREATE TABLE ApplicationSettings(Id INTEGER NOT NULL, CompanyName TEXT NOT NULL PRIMARY KEY, CreatedAtUtc TEXT NOT NULL, UpdatedAtUtc TEXT NOT NULL);",
            "missing-index" => "DROP INDEX IX_Sales_SaleDate;",
            "nonunique-index" => "DROP INDEX IX_Users_Username; CREATE INDEX IX_Users_Username ON Users(Username);",
            "wrong-index-key" => "DROP INDEX IX_Users_Username; CREATE UNIQUE INDEX IX_Users_Username ON Users(FullName);",
            "index-order" => "DROP INDEX IX_UserPermissions_UserId_Module; CREATE UNIQUE INDEX IX_UserPermissions_UserId_Module ON UserPermissions(Module,UserId);",
            "index-collation" => "DROP INDEX IX_Users_Username; CREATE UNIQUE INDEX IX_Users_Username ON Users(Username COLLATE NOCASE);",
            "partial-index" => "DROP INDEX IX_Users_Username; CREATE UNIQUE INDEX IX_Users_Username ON Users(Username) WHERE IsActive=1;",
            "missing-singleton" => "DROP INDEX UX_TreasuryOpeningBalance_Singleton;",
            "nonunique-singleton" => "DROP INDEX UX_TreasuryOpeningBalance_Singleton; CREATE INDEX UX_TreasuryOpeningBalance_Singleton ON TreasuryOpeningBalance ((1));",
            "wrong-singleton-key" => "DROP INDEX UX_TreasuryOpeningBalance_Singleton; CREATE UNIQUE INDEX UX_TreasuryOpeningBalance_Singleton ON TreasuryOpeningBalance(Id);",
            "partial-singleton" => "DROP INDEX UX_TreasuryOpeningBalance_Singleton; CREATE UNIQUE INDEX UX_TreasuryOpeningBalance_Singleton ON TreasuryOpeningBalance ((1)) WHERE CashAmountMinor > 0;",
            "singleton-null-expression" => "DROP INDEX UX_TreasuryOpeningBalance_Singleton; CREATE UNIQUE INDEX UX_TreasuryOpeningBalance_Singleton ON TreasuryOpeningBalance ((NULL));",
            "singleton-descending" => "DROP INDEX UX_TreasuryOpeningBalance_Singleton; CREATE UNIQUE INDEX UX_TreasuryOpeningBalance_Singleton ON TreasuryOpeningBalance ((1) DESC);",
            "extra-table" => "CREATE TABLE Unexpected(Id INTEGER PRIMARY KEY);",
            "extra-column" => "ALTER TABLE Clients ADD COLUMN Unexpected TEXT;",
            "extra-trigger" => "CREATE TRIGGER Unexpected AFTER INSERT ON Clients BEGIN DELETE FROM Clients WHERE Id=NEW.Id; END;",
            "declared-type" or "nullability" or "missing-fk" or "fk-delete" or "check-weakened" or "xor-weakened" => "SELECT 1;",
            _ => throw new ArgumentOutOfRangeException(nameof(fault))
        };
        await ExecuteAsync(database, sql);
        switch (fault)
        {
            case "declared-type":
                await RewriteSchemaAsync(database, "TreasuryOpeningBalance", "\"CashAmountMinor\" INTEGER", "\"CashAmountMinor\" REAL"); break;
            case "nullability":
                await RewriteSchemaAsync(database, "Clients", "\"Name\" TEXT NOT NULL", "\"Name\" TEXT NULL"); break;
            case "missing-fk":
                await RewriteSchemaAsync(database, "UserPermissions", ",\n    CONSTRAINT \"FK_UserPermissions_Users_UserId\" FOREIGN KEY (\"UserId\") REFERENCES \"Users\" (\"Id\") ON DELETE RESTRICT", "");
                Assert.Equal(0, await ScalarLongAsync(database, "SELECT COUNT(*) FROM pragma_foreign_key_list('UserPermissions');")); break;
            case "fk-delete":
                await RewriteSchemaAsync(database, "Sales", "ON DELETE RESTRICT", "ON DELETE CASCADE"); break;
            case "check-weakened":
                await RewriteSchemaAsync(database, "Expenses", "\"AmountMinor\" > 0", "\"AmountMinor\" >= 0"); break;
            case "xor-weakened":
                await RewriteSchemaAsync(database, "CustomerPaymentAllocations", "\"InitialCustomerReceivableId\" IS NULL", "\"InitialCustomerReceivableId\" IS NOT NULL"); break;
        }
        // The malicious artifact has a correct ZIP, manifest, length and SHA-256.
        // SQLite itself considers these falsified empty schemas internally sound.
        Assert.Equal("ok", await ScalarStringAsync(database, "PRAGMA integrity_check;"));
        Assert.Equal(0, await ScalarLongAsync(database, "SELECT COUNT(*) FROM pragma_foreign_key_check;"));
        var artifact = await PackDatabaseAsync(database);
        var artifactHash = await HashFileAsync(artifact);
        var hashBefore = await HashFileAsync(fixture.LivePath);
        var swapped = false;
        fixture.RestoreTestHook.AfterAtomicReplace = _ => swapped = true;
        var result = await fixture.Service.RestoreAsync(new(artifact));
        output.WriteLine($"{fault}: Success={result.Success}; Failure={result.FailureReason}; swap={swapped}");
        Assert.False(result.Success);
        Assert.Equal(fault is "unknown-migration" or "migration-gap" or "missing-first-migration"
            ? DatabaseBackupFailureReason.IncompatibleBackup : DatabaseBackupFailureReason.InvalidBackup, result.FailureReason);
        Assert.False(swapped);
        Assert.Null(result.PreRestoreBackupPath);
        Assert.False(result.RestartRequired);
        Assert.True(fixture.Session.IsAuthenticated);
        Assert.Equal(hashBefore, await HashFileAsync(fixture.LivePath));
        Assert.Equal(artifactHash, await HashFileAsync(artifact));
        await fixture.AssertLiveMarkerAsync();
        Assert.Empty(Directory.GetFiles(fixture.Root, "FinancialManagement_Restore*"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Restore_AcceptsExactSupportedSchemaAndMigratesOnlyStaging(int version)
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var database = Path.Combine(fixture.Root, "supported.db");
        await CreateApplicationDatabaseAsync(database, version, activeResponsible: true, clientName: "Historical data");
        var artifact = await PackDatabaseAsync(database);
        var sourceHash = await HashFileAsync(artifact);
        var result = await fixture.Service.RestoreAsync(new(artifact));
        Assert.True(result.Success);
        Assert.True(result.RestartRequired);
        Assert.False(fixture.Session.IsAuthenticated);
        Assert.Equal(sourceHash, await HashFileAsync(artifact));
        Assert.Equal(version, await ScalarLongAsync(database, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        Assert.Equal(4, await ScalarLongAsync(fixture.LivePath, "SELECT COUNT(*) FROM __EFMigrationsHistory;"));
        Assert.Equal(1, await ScalarLongAsync(fixture.LivePath, "SELECT COUNT(*) FROM Clients WHERE Name='Historical data';"));
        await using var live = NewConnection(fixture.LivePath);
        await live.OpenAsync();
        Assert.True(await new DatabaseSchemaValidator(fixture.Context).ValidateAsync(live,
            fixture.Context.Database.GetMigrations().ToArray(), CancellationToken.None));
    }

    [Fact]
    public async Task Restore_AcceptsSqliteFormattingAndInternalStatisticsAndUnorderedHistoryInsertion()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var database = Path.Combine(fixture.Root, "legitimate.db");
        await CreateApplicationDatabaseAsync(database, 4, activeResponsible: true);
        await ExecuteAsync(database,
            "DROP INDEX UX_TreasuryOpeningBalance_Singleton; create unique index [UX_TreasuryOpeningBalance_Singleton] on `TreasuryOpeningBalance` (/* constant */(1)); ANALYZE;");
        await RewriteSchemaAsync(database, "ApplicationSettings", "\"CompanyName\" TEXT NOT NULL", "[CompanyName] /* legitimate formatting */ text not null");
        var migrations = fixture.Context.Database.GetMigrations().Reverse().ToArray();
        await ExecuteAsync(database, "DELETE FROM __EFMigrationsHistory;" + string.Concat(migrations.Select(id =>
            $"INSERT INTO __EFMigrationsHistory VALUES ('{id}','10.0.11');")));
        Assert.True((await fixture.Service.RestoreAsync(new(await PackDatabaseAsync(database)))).Success);
    }

    [Fact]
    public async Task SchemaValidation_IsReadOnlyAndRechecksMetadataWithoutStaleCache()
    {
        await using var fixture = await BackupFixture.CreateAsync(authenticate: true);
        var database = Path.Combine(fixture.Root, "metadata.db");
        await CreateApplicationDatabaseAsync(database, 4, activeResponsible: true);
        var validator = new DatabaseSchemaValidator(fixture.Context, new SchemaTestLogger(output));
        var migrations = fixture.Context.Database.GetMigrations().ToArray();
        var before = await HashFileAsync(database);
        Assert.True(await ValidateReadOnlyAsync());
        Assert.Equal(before, await HashFileAsync(database));
        await ExecuteAsync(database, "DROP INDEX IX_Sales_SaleDate;");
        var changed = await HashFileAsync(database);
        Assert.False(await ValidateReadOnlyAsync());
        Assert.Equal(changed, await HashFileAsync(database));

        async Task<bool> ValidateReadOnlyAsync()
        {
            await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = database, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
            await connection.OpenAsync();
            return await validator.ValidateAsync(connection, migrations, CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("CHECK (\"AmountMinor\" > 0)", "CHECK (AmountMinor > 0)", true)]
    [InlineData("CHECK (\"AmountMinor\" > 0)", "CHECK (AmountMinor > '0')", false)]
    [InlineData("CHECK (\"AmountMinor\" IS NULL)", "CHECK (AmountMinor IS \"NULL\")", false)]
    [InlineData("CHECK (\"AmountMinor\" <> 0)", "CHECK (AmountMinor < > 0)", false)]
    [InlineData("CHECK (\"AmountMinor\" > 0)", "CHECK (AmountMinor > 0 OR 1)", false)]
    public void SchemaSql_DistinguishesExpressionsLiteralsAndQuotedKeywords(string expected, string actual, bool matches)
    {
        var identifiers = new HashSet<string>(["AmountMinor"], StringComparer.OrdinalIgnoreCase);
        Assert.Equal(matches, DatabaseSchemaSql.Matches(expected, actual, false, identifiers));
    }

    // Fixture-only alteration of an empty table's stored declaration. The next
    // connection reparses the changed schema; no production validator writes SQL.
    private static async Task RewriteSchemaAsync(string database, string table, string oldText, string newText)
    {
        await using var connection = NewConnection(database);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT sql FROM sqlite_master WHERE type='table' AND name=$name;";
        command.Parameters.AddWithValue("$name", table);
        var sql = Assert.IsType<string>(await command.ExecuteScalarAsync()).Replace("\r\n", "\n", StringComparison.Ordinal);
        Assert.Contains(oldText, sql, StringComparison.Ordinal);
        command.CommandText = "PRAGMA schema_version;";
        var schemaVersion = Convert.ToInt64(await command.ExecuteScalarAsync());
        command.Parameters.AddWithValue("$sql", sql.Replace(oldText, newText, StringComparison.Ordinal));
        command.CommandText = $"PRAGMA writable_schema=ON; UPDATE sqlite_master SET sql=$sql WHERE type='table' AND name=$name; PRAGMA schema_version={schemaVersion + 1}; PRAGMA writable_schema=OFF;";
        await command.ExecuteNonQueryAsync();
    }

    private sealed class SchemaTestLogger(ITestOutputHelper output) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => output.WriteLine(formatter(state, exception));
    }
}
