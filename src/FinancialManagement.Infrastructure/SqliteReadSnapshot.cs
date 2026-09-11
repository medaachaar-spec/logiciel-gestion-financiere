using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure;

internal static class SqliteReadSnapshot
{
    public static async Task<T> ExecuteAsync<T>(
        FinancialManagementDbContext dbContext,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(operation);

        if (dbContext.Database.CurrentTransaction is not null)
            return await operation(cancellationToken);

        await dbContext.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: true);
            await using var attachment = await dbContext.Database.UseTransactionAsync(
                transaction, cancellationToken);
            var result = await operation(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        finally
        {
            await dbContext.Database.CloseConnectionAsync();
        }
    }
}
