using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure;

/// <summary>Startup boundary: validate legacy storage, migrate, validate current storage.</summary>
public static class FinancialDatabaseStartup
{
    public static async Task InitializeAsync(
        FinancialManagementDbContext context, CancellationToken cancellationToken = default)
    {
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            var connection = (SqliteConnection)context.Database.GetDbConnection();
            if (!await FinancialStorageValidator.ValidateAsync(connection, allowEmpty: true, cancellationToken))
                throw new FinancialStorageIntegrityException();
            await context.Database.MigrateAsync(cancellationToken);
            if (!await FinancialStorageValidator.ValidateAsync(connection, allowEmpty: false, cancellationToken))
                throw new FinancialStorageIntegrityException();
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}

public sealed class FinancialStorageIntegrityException : Exception
{
    public FinancialStorageIntegrityException()
        : base("Financial storage integrity validation failed.") { }
}
