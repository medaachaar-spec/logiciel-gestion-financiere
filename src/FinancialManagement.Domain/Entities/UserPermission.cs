using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Domain.Entities;

public sealed class UserPermission
{
    public long Id { get; set; }
    public long UserId { get; set; }
    public PermissionModule Module { get; set; }
    public bool CanView { get; set; }
    public bool CanCreate { get; set; }
    public bool CanModify { get; set; }
}
