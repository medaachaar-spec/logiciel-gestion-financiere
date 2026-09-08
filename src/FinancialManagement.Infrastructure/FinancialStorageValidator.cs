using Microsoft.Data.Sqlite;

namespace FinancialManagement.Infrastructure;

/// <summary>Read-only SQLite storage validation, before any EF monetary materialization.</summary>
internal static class FinancialStorageValidator
{
    private static readonly (string Table, string[] Columns)[] FinancialColumns =
    [
        ("Sales", ["TotalAmountMinor"]),
        ("Purchases", ["TotalAmountMinor"]),
        ("Expenses", ["AmountMinor"]),
        ("CustomerPayments", ["AmountMinor"]),
        ("CustomerPaymentAllocations", ["AmountMinor"]),
        ("InitialCustomerReceivables", ["InitialAmountMinor"]),
        ("SupplierPayments", ["AmountMinor"]),
        ("SupplierPaymentAllocations", ["AmountMinor"]),
        ("InitialSupplierPayables", ["InitialAmountMinor"]),
        ("TreasuryOpeningBalance", ["CashAmountMinor", "BankAmountMinor"]),
        ("TreasuryTransfers", ["AmountMinor"])
    ];

    // The caller owns the open connection. A single read transaction gives all
    // table scans a consistent snapshot; it cannot police malicious later writers.
    internal static async Task<bool> ValidateAsync(
        SqliteConnection connection, bool allowEmpty, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var transaction = connection.BeginTransaction(deferred: true);
            var tables = new HashSet<string>(StringComparer.Ordinal);
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                while (await reader.ReadAsync(cancellationToken))
                    tables.Add(reader.GetString(0));
            }

            // Fresh EF bookkeeping is allowed only with zero applied migrations
            // and no application tables. Never hide a missing table in a legacy DB.
            var hasHistory = tables.Contains("__EFMigrationsHistory");
            long historyCount = 0;
            if (hasHistory)
            {
                await using var history = connection.CreateCommand();
                history.Transaction = transaction;
                history.CommandText = "SELECT COUNT(*) FROM \"__EFMigrationsHistory\";";
                historyCount = Convert.ToInt64(await history.ExecuteScalarAsync(cancellationToken));
            }
            if (historyCount == 0)
                return allowEmpty && tables.All(t => t is "__EFMigrationsHistory" or "__EFMigrationsLock");

            foreach (var (table, columns) in FinancialColumns)
            {
                if (!tables.Contains(table))
                    return false;
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                // Qualified names prevent SQLite's legacy double-quoted-string
                // fallback from concealing a missing column in an empty table.
                command.CommandText = $"SELECT EXISTS(SELECT 1 FROM \"{table}\" AS f WHERE "
                    + string.Join(" OR ", columns.Select(c => $"typeof(f.\"{c}\") <> 'integer'")) + ");";
                if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 0)
                    return false;
            }
            return true;
        }
        catch (SqliteException)
        {
            // Missing columns, malformed schema and unreadable databases fail closed.
            return false;
        }
    }
}
