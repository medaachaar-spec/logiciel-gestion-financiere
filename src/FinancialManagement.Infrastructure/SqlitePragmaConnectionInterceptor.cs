using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FinancialManagement.Infrastructure;

internal sealed class SqlitePragmaConnectionInterceptor : DbConnectionInterceptor
{
    public static SqlitePragmaConnectionInterceptor Instance { get; } = new();

    private static readonly string[] Pragmas =
    [
        "PRAGMA foreign_keys = ON",
        "PRAGMA journal_mode = WAL",
        "PRAGMA synchronous = FULL",
        "PRAGMA busy_timeout = 5000"
    ];

    private SqlitePragmaConnectionInterceptor()
    {
    }

    public override void ConnectionOpened(
        DbConnection connection,
        ConnectionEndEventData eventData)
    {
        base.ConnectionOpened(connection, eventData);
        ApplyPragmas(connection);
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
        await ApplyPragmasAsync(connection, cancellationToken);
    }

    private static void ApplyPragmas(DbConnection connection)
    {
        using var command = connection.CreateCommand();

        foreach (var pragma in Pragmas)
        {
            command.CommandText = pragma;
            command.Parameters.Clear();
            command.ExecuteNonQuery();
        }
    }

    private static async Task ApplyPragmasAsync(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();

        foreach (var pragma in Pragmas)
        {
            command.CommandText = pragma;
            command.Parameters.Clear();
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
