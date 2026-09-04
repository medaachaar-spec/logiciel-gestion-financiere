using FinancialManagement.Application.Authentication;
using Microsoft.AspNetCore.Identity;

namespace FinancialManagement.Infrastructure.Authentication;

internal sealed class MicrosoftIdentityPasswordHasher : IPasswordHasher
{
    private static readonly object PasswordHashOwner = new();
    private readonly PasswordHasher<object> _passwordHasher = new();

    public string HashPassword(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);
        return _passwordHasher.HashPassword(PasswordHashOwner, password);
    }

    public bool VerifyPassword(string passwordHash, string providedPassword)
    {
        if (string.IsNullOrEmpty(passwordHash) || string.IsNullOrEmpty(providedPassword))
        {
            return false;
        }

        try
        {
            return _passwordHasher.VerifyHashedPassword(
                    PasswordHashOwner,
                    passwordHash,
                    providedPassword)
                is not PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
