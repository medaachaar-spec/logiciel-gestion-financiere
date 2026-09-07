namespace FinancialManagement.Application.Administration;

// Keep credentials out of automatically generated string representations.
public sealed class CreateEmployeeCommand(string fullName, string username, string password)
{
    public string FullName { get; } = fullName;
    public string Username { get; } = username;
    public string Password { get; } = password;
}
