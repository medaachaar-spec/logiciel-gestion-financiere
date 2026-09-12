using FinancialManagement.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Tests;

public sealed class FoundationDatabaseIsolationTests
{
    [Fact]
    public void Design_time_context_uses_only_an_in_memory_database()
    {
        using var context = new FinancialManagementDesignTimeDbContextFactory()
            .CreateDbContext([]);
        var connection = Assert.IsType<SqliteConnection>(context.Database.GetDbConnection());
        var connectionString = new SqliteConnectionStringBuilder(connection.ConnectionString);

        Assert.Equal(":memory:", connectionString.DataSource);
    }
}
