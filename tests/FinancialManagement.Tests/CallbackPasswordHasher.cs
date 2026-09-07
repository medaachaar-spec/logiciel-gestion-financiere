using FinancialManagement.Application.Authentication;
using FinancialManagement.Infrastructure.Authentication;

namespace FinancialManagement.Tests;

// Test-only decorator; production always uses the existing canonical hasher.
internal sealed class CallbackPasswordHasher(Action afterHash) : IPasswordHasher
{
    private readonly MicrosoftIdentityPasswordHasher _inner = new();

    public string HashPassword(string password)
    {
        var hash = _inner.HashPassword(password);
        afterHash();
        return hash;
    }

    public bool VerifyPassword(string passwordHash, string password) => _inner.VerifyPassword(passwordHash, password);
}
