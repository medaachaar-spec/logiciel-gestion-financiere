using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Domain.Entities;

public sealed class User
{
    public long Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }

    public ICollection<UserPermission> UserPermissions { get; set; } = [];
}
