using FinancialManagement.Application.Authentication;
using FinancialManagement.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class PasswordHasherTests
{
    private const string Password = "Test-only password 42!";

    [Fact]
    public void HashPassword_does_not_return_plaintext()
    {
        using var serviceProvider = CreateServiceProvider();
        var passwordHasher = serviceProvider.GetRequiredService<IPasswordHasher>();

        var passwordHash = passwordHasher.HashPassword(Password);

        Assert.NotEmpty(passwordHash);
        Assert.NotEqual(Password, passwordHash);
    }

    [Fact]
    public void VerifyPassword_accepts_correct_password()
    {
        using var serviceProvider = CreateServiceProvider();
        var passwordHasher = serviceProvider.GetRequiredService<IPasswordHasher>();
        var passwordHash = passwordHasher.HashPassword(Password);

        Assert.True(passwordHasher.VerifyPassword(passwordHash, Password));
    }

    [Fact]
    public void VerifyPassword_rejects_incorrect_password()
    {
        using var serviceProvider = CreateServiceProvider();
        var passwordHasher = serviceProvider.GetRequiredService<IPasswordHasher>();
        var passwordHash = passwordHasher.HashPassword(Password);

        Assert.False(passwordHasher.VerifyPassword(passwordHash, "Incorrect test password"));
    }

    [Fact]
    public void HashPassword_uses_a_random_salt()
    {
        using var serviceProvider = CreateServiceProvider();
        var passwordHasher = serviceProvider.GetRequiredService<IPasswordHasher>();

        var firstHash = passwordHasher.HashPassword(Password);
        var secondHash = passwordHasher.HashPassword(Password);

        Assert.NotEqual(firstHash, secondHash);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-password-hash")]
    [InlineData("AQIDBA==")]
    public void VerifyPassword_rejects_invalid_hash_without_throwing(string invalidHash)
    {
        using var serviceProvider = CreateServiceProvider();
        var passwordHasher = serviceProvider.GetRequiredService<IPasswordHasher>();

        var exception = Record.Exception(() =>
        {
            Assert.False(passwordHasher.VerifyPassword(invalidHash, Password));
        });

        Assert.Null(exception);
    }

    private static ServiceProvider CreateServiceProvider() =>
        new ServiceCollection()
            .AddInfrastructure("Data Source=:memory:")
            .BuildServiceProvider();
}
