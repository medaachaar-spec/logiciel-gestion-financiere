namespace FinancialManagement.Application.Initialization;

// Not a record: the generated record ToString must never include a password.
public sealed class BootstrapResponsibleCommand(string fullName, string username, string password)
{
    public string FullName { get; } = fullName;
    public string Username { get; } = username;
    public string Password { get; } = password;
}
