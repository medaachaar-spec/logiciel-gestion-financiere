using System.Data.Common;

namespace FinancialManagement.Tests;

internal static class SessionValidationCommandTestHelper
{
    internal static bool IsPersistedSessionValidation(DbCommand command) =>
        IsPersistedSessionValidation(command.CommandText);

    internal static bool IsPersistedSessionValidation(string sql) =>
        sql.Contains("Users", StringComparison.OrdinalIgnoreCase)
        && sql.Contains("PasswordHash", StringComparison.OrdinalIgnoreCase)
        && sql.Contains("UpdatedAtUtc", StringComparison.OrdinalIgnoreCase)
        && sql.Contains("Username", StringComparison.OrdinalIgnoreCase)
        && sql.Contains("Role", StringComparison.OrdinalIgnoreCase)
        && sql.Contains("IsActive", StringComparison.OrdinalIgnoreCase);
}