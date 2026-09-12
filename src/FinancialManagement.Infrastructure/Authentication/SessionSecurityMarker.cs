using System.Security.Cryptography;
using System.Text;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Infrastructure.Authentication;

internal static class SessionSecurityMarker
{
    public static string Create(
        long userId,
        string username,
        UserRole role,
        bool isActive,
        string passwordHash,
        DateTimeOffset updatedAtUtc)
    {
        var value = string.Join(
            '\n',
            userId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            username,
            role.ToString(),
            isActive ? "1" : "0",
            passwordHash,
            updatedAtUtc.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture));

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    }
}
