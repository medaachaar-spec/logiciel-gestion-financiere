using System.Data.Common;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class FinancialManagementDbContextTests
{
    [Fact]
    public void Context_can_execute_sqlite_query_with_temporary_database()
    {
        var databasePath = CreateTemporaryDatabasePath();

        try
        {
            using (var serviceProvider = new ServiceCollection()
                .AddInfrastructure($"Data Source={databasePath};Pooling=False")
                .BuildServiceProvider())
            using (var context = serviceProvider.GetRequiredService<FinancialManagementDbContext>())
            {
                Assert.Equal(
                    "Microsoft.EntityFrameworkCore.Sqlite",
                    context.Database.ProviderName);

                context.Database.OpenConnection();
                var result = context.Database.SqlQueryRaw<int>("SELECT 1 AS Value").Single();

                Assert.Equal(1, result);
                context.Database.CloseConnection();
            }
        }
        finally
        {
            DeleteTemporaryDatabase(databasePath);
        }
    }

    [Fact]
    public void Context_applies_required_sqlite_pragmas_when_connection_opens()
    {
        var databasePath = CreateTemporaryDatabasePath();

        try
        {
            using (var serviceProvider = new ServiceCollection()
                .AddInfrastructure($"Data Source={databasePath};Pooling=False")
                .BuildServiceProvider())
            using (var context = serviceProvider.GetRequiredService<FinancialManagementDbContext>())
            {
                context.Database.OpenConnection();
                var connection = context.Database.GetDbConnection();

                Assert.Equal(1L, ExecuteScalar<long>(connection, "PRAGMA foreign_keys"));
                Assert.Equal("wal", ExecuteScalar<string>(connection, "PRAGMA journal_mode"));
                Assert.Equal(2L, ExecuteScalar<long>(connection, "PRAGMA synchronous"));
                Assert.Equal(5000L, ExecuteScalar<long>(connection, "PRAGMA busy_timeout"));

                context.Database.CloseConnection();
            }
        }
        finally
        {
            DeleteTemporaryDatabase(databasePath);
        }
    }

    private static T ExecuteScalar<T>(DbConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        var value = command.ExecuteScalar();
        ArgumentNullException.ThrowIfNull(value);
        return (T)Convert.ChangeType(value, typeof(T));
    }

    private static string CreateTemporaryDatabasePath() =>
        Path.Combine(Path.GetTempPath(), $"FinancialManagement-{Guid.NewGuid():N}.db");

    private static void DeleteTemporaryDatabase(string databasePath)
    {
        foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
