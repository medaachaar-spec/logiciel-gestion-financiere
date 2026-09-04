using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.Authorization;

public interface IAuthorizationService
{
    Task<bool> CanAsync(
        PermissionModule module,
        PermissionAction action,
        CancellationToken cancellationToken = default);

    Task<EffectiveModulePermission> GetEffectivePermissionsAsync(
        PermissionModule module,
        CancellationToken cancellationToken = default);

    bool CanAccessResponsibleCapability(ResponsibleCapability capability);
}
