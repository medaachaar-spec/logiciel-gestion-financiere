namespace FinancialManagement.Application.Authentication;

public interface IPasswordHasher
{
    string HashPassword(string password);

    bool VerifyPassword(string passwordHash, string providedPassword);
}
