using System.Security.Cryptography;
using System.Text;
using FinancialManagement.Infrastructure.Migrations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Logging;

namespace FinancialManagement.Infrastructure.Backup;

/// <summary>
/// Read-only, per-validation schema contract from the compiled migration's target
/// model. No reference database, migrations, seed execution or mutable global cache.
/// SQL supplements must be reviewed when a migration changes unmodelled objects.
/// </summary>
internal sealed class DatabaseSchemaValidator(FinancialManagementDbContext context, ILogger? logger = null)
{
    private const string History = "__EFMigrationsHistory";
    private const string Lock = "__EFMigrationsLock";
    private const string Singleton = "UX_TreasuryOpeningBalance_Singleton";

    public async Task<bool> ValidateHistoryAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        var objects = await ReadObjectsAsync(connection, cancellationToken);
        var identifiers = new HashSet<string>([History, "MigrationId", "ProductVersion", "PK_" + History], StringComparer.OrdinalIgnoreCase);
        return objects.TryGetValue(History, out var history) && history.Type == "table" && history.Sql is not null &&
            DatabaseSchemaSql.Matches(context.GetService<IHistoryRepository>().GetCreateScript(), history.Sql, true, identifiers);
    }

    public async Task<bool> ValidateAsync(SqliteConnection connection, IReadOnlyList<string> applied,
        CancellationToken cancellationToken)
    {
        var assembly = context.GetService<IMigrationsAssembly>();
        var provider = context.Database.ProviderName!;
        var migration = assembly.CreateMigration(assembly.Migrations[applied[^1]], provider);
        var model = context.GetService<IModelRuntimeInitializer>().Initialize(migration.TargetModel, designTime: true);
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        var generator = context.GetService<IMigrationsSqlGenerator>();
        // SQLite's SQL generator lifts inline PKs by mutating its operations.
        // Keep a separate operation graph so metadata checks retain the real PK.
        var sqlOperations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, model.GetRelationalModel());
        var expected = new Dictionary<string, SchemaObject>(StringComparer.Ordinal);
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            expected.Add(table.Name, new("table", table.Name, SqlFor(table)));
            identifiers.Add(table.Name);
            foreach (var column in table.Columns) identifiers.Add(column.Name);
            if (table.PrimaryKey is not null) identifiers.Add(table.PrimaryKey.Name!);
            foreach (var constraint in table.ForeignKeys) identifiers.Add(constraint.Name!);
            foreach (var constraint in table.CheckConstraints) identifiers.Add(constraint.Name!);
            foreach (var constraint in table.UniqueConstraints) identifiers.Add(constraint.Name!);
        }
        foreach (var index in indexes)
        {
            identifiers.Add(index.Name);
            expected.Add(index.Name, new("index", index.Table, SqlFor(index)));
        }
        expected.Add(History, new("table", History, context.GetService<IHistoryRepository>().GetCreateScript()));
        foreach (var name in new[] { History, "MigrationId", "ProductVersion", "PK_" + History, Lock, "Timestamp", "PK_" + Lock })
            identifiers.Add(name);

        // 003 deliberately uses SQL outside the EF target model. Read its actual
        // operation; never accept a matching index name without its definition.
        // New raw-SQL migrations require a local, explicit contract review here.
        var hasSingleton = false;
        foreach (var id in applied)
        {
            var source = assembly.CreateMigration(assembly.Migrations[id], provider);
            // 004 has a frozen, explicitly reviewed SQL contract. Only this exact
            // non-suppressed operation is accepted; final tables/CHECKs/indexes/FKs
            // are still compared below against its compiled target model plus 003.
            if (source is _004_EnforceFinancialIntegerStorageClass)
            {
                if (!HasApprovedStorageMigrationSql(source))
                    return false;
                continue;
            }
            foreach (var sql in source.UpOperations.OfType<SqlOperation>())
            {
                if (source is not _003_TreasuryOpeningBalanceSingleton || hasSingleton) return false;
                hasSingleton = true;
                identifiers.Add(Singleton);
                expected.Add(Singleton, new("index", "TreasuryOpeningBalance", sql.Sql));
            }
        }

        var actual = await ReadObjectsAsync(connection, cancellationToken);
        foreach (var (name, item) in actual)
        {
            if (IsSqliteInternal(name, item)) continue;
            if (name == Lock)
            {
                // Optional provider bookkeeping, not an application table. EF10
                // leaves the empty lock table behind after successful migration.
                const string lockSql = "CREATE TABLE \"__EFMigrationsLock\" (\"Id\" INTEGER NOT NULL CONSTRAINT \"PK___EFMigrationsLock\" PRIMARY KEY, \"Timestamp\" TEXT NOT NULL);";
                if (item.Type != "table" || item.Sql is null || !DatabaseSchemaSql.Matches(lockSql, item.Sql, true, identifiers)) return Reject(name);
                continue;
            }
            if (!expected.TryGetValue(name, out var contract) || item.Type != contract.Type || item.Table != contract.Table ||
                item.Sql is null || !DatabaseSchemaSql.Matches(contract.Sql!, item.Sql, item.Type == "table", identifiers)) return Reject(name);
        }
        if (expected.Keys.Any(name => !actual.ContainsKey(name))) return false;

        foreach (var table in tables)
        {
            if (!await ColumnsMatchAsync(connection, table, cancellationToken) ||
                !await ForeignKeysMatchAsync(connection, table, cancellationToken) ||
                !await IndexesMatchAsync(connection, table.Name, indexes.Where(index => index.Table == table.Name),
                    hasSingleton && table.Name == "TreasuryOpeningBalance", cancellationToken)) return Reject(table.Name + " metadata");
        }
        return true;

        string SqlFor(MigrationOperation operation)
        {
            var sqlOperation = operation switch
            {
                CreateTableOperation table => (MigrationOperation)sqlOperations.OfType<CreateTableOperation>().Single(item => item.Name == table.Name),
                CreateIndexOperation index => sqlOperations.OfType<CreateIndexOperation>().Single(item => item.Name == index.Name),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            return string.Concat(generator.Generate([sqlOperation], model).Select(command => command.CommandText));
        }
    }

    internal static bool HasApprovedStorageMigrationSql(Migration migration) =>
        migration is _004_EnforceFinancialIntegerStorageClass
        && migration.UpOperations.Count == 1
        && migration.UpOperations[0] is SqlOperation { SuppressTransaction: false } operation
        // Fingerprint of the reviewed, frozen 004 Up operation (LF-normalized).
        && Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(operation.Sql.Replace("\r\n", "\n"))))
            == "352673232CDDE978D8C99B279435B991D14B56D21919C16A12CC6354B992A526";

    private bool Reject(string name)
    {
        logger?.LogWarning("SQLite schema object {SchemaObject} differs from the supported migration contract.", name);
        return false;
    }

    private static bool IsSqliteInternal(string name, SchemaObject item) =>
        // SQLite reserves these names. Do not allow arbitrary "sqlite_*" objects
        // or arbitrary EF-prefixed objects to hide unexpected application schema.
        (item.Type == "table" && name is "sqlite_sequence" or "sqlite_stat1" or "sqlite_stat4") ||
        (item.Type == "index" && item.Sql is null && name.StartsWith("sqlite_autoindex_", StringComparison.Ordinal));

    private static async Task<bool> ColumnsMatchAsync(SqliteConnection connection, CreateTableOperation table, CancellationToken ct)
    {
        await using var command = Command(connection, "SELECT name,type,\"notnull\",pk,hidden FROM pragma_table_xinfo($name);", table.Name);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var count = 0;
        while (await reader.ReadAsync(ct))
        {
            count++;
            var column = table.Columns.SingleOrDefault(column => column.Name == reader.GetString(0));
            var pk = Array.IndexOf(table.PrimaryKey?.Columns ?? [], reader.GetString(0)) + 1;
            if (column is null || !string.Equals(column.ColumnType, reader.GetString(1), StringComparison.OrdinalIgnoreCase) ||
                reader.GetBoolean(2) == column.IsNullable || reader.GetInt32(3) != pk || reader.GetInt32(4) != 0) return false;
        }
        return count == table.Columns.Count;
    }

    private static async Task<bool> ForeignKeysMatchAsync(SqliteConnection connection, CreateTableOperation table, CancellationToken ct)
    {
        // Group by FK id (SQLite's ordering is unspecified), preserve composite-key
        // order within each group. Names and deferred clauses are checked in DDL.
        await using var command = Command(connection, "SELECT id,seq,\"table\",\"from\",\"to\",on_update,on_delete,\"match\" FROM pragma_foreign_key_list($name) ORDER BY id,seq;", table.Name);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var groups = new Dictionary<long, List<string>>();
        while (await reader.ReadAsync(ct))
        {
            var id = reader.GetInt64(0);
            if (!groups.TryGetValue(id, out var group)) groups[id] = group = [];
            group.Add(string.Join('|', Enumerable.Range(1, 7).Select(index => Convert.ToString(reader.GetValue(index)))));
        }
        var expected = table.ForeignKeys.Select(fk => string.Join(';', fk.Columns.Select((column, i) =>
            $"{i}|{fk.PrincipalTable}|{column}|{fk.PrincipalColumns![i]}|{Action(fk.OnUpdate)}|{Action(fk.OnDelete)}|NONE"))).Order(StringComparer.Ordinal);
        return expected.SequenceEqual(groups.Values.Select(group => string.Join(';', group)).Order(StringComparer.Ordinal));
    }

    private static string Action(ReferentialAction action) => action switch
    {
        ReferentialAction.NoAction => "NO ACTION",
        ReferentialAction.Restrict => "RESTRICT",
        ReferentialAction.Cascade => "CASCADE",
        ReferentialAction.SetNull => "SET NULL",
        ReferentialAction.SetDefault => "SET DEFAULT",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static async Task<bool> IndexesMatchAsync(SqliteConnection connection, string table,
        IEnumerable<CreateIndexOperation> indexes, bool singleton, CancellationToken ct)
    {
        var expected = indexes.ToDictionary(index => index.Name, StringComparer.Ordinal);
        var found = new List<(string Name, bool Unique, string Origin, bool Partial)>();
        await using (var command = Command(connection, "SELECT name,\"unique\",origin,partial FROM pragma_index_list($name);", table))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) found.Add((reader.GetString(0), reader.GetBoolean(1), reader.GetString(2), reader.GetBoolean(3)));
        if (found.Count != expected.Count + (singleton ? 1 : 0)) return false;
        foreach (var item in found)
        {
            var isSingleton = singleton && item.Name == Singleton;
            expected.TryGetValue(item.Name, out var index);
            if ((!isSingleton && index is null) || item.Origin != "c" ||
                item.Unique != (isSingleton || index!.IsUnique) || item.Partial != (!isSingleton && index!.Filter is not null)) return false;
            await using var command = Command(connection, "SELECT cid,name,desc,coll FROM pragma_index_xinfo($name) WHERE key=1 ORDER BY seqno;", item.Name);
            await using var reader = await command.ExecuteReaderAsync(ct);
            var position = 0;
            while (await reader.ReadAsync(ct))
            {
                if (isSingleton)
                {
                    if (position != 0 || reader.GetInt32(0) != -2 || !reader.IsDBNull(1) || reader.GetBoolean(2)) return false;
                }
                else
                {
                    if (position >= index!.Columns.Length || reader.GetInt32(0) < 0 || reader.GetString(1) != index.Columns[position] ||
                        reader.GetBoolean(2) != (index.IsDescending is { } descending && (descending.Length == 0 || descending[position]))) return false;
                }
                if (!string.Equals(reader.GetString(3), "BINARY", StringComparison.OrdinalIgnoreCase)) return false;
                position++;
            }
            if (position != (isSingleton ? 1 : index!.Columns.Length)) return false;
        }
        return true;
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, string name)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$name", name);
        return command;
    }

    private static async Task<Dictionary<string, SchemaObject>> ReadObjectsAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT name,type,tbl_name,sql FROM sqlite_master;";
        await using var reader = await command.ExecuteReaderAsync(ct);
        var objects = new Dictionary<string, SchemaObject>(StringComparer.Ordinal);
        while (await reader.ReadAsync(ct))
            objects.Add(reader.GetString(0), new(reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        return objects;
    }

    private sealed record SchemaObject(string Type, string Table, string? Sql);
}
