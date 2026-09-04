using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Authorization;

internal sealed class AuthorizationService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession) : IAuthorizationService
{
    private static readonly EffectiveModulePermission NoPermissions = new(false, false, false);
    private static readonly EffectiveModulePermission AllPermissions = new(true, true, true);

    public async Task<bool> CanAsync(
        PermissionModule module,
        PermissionAction action,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(action))
        {
            return false;
        }

        var permissions = await GetEffectivePermissionsAsync(module, cancellationToken);

        return action switch
        {
            PermissionAction.View => permissions.CanView,
            PermissionAction.Create => permissions.CanCreate,
            PermissionAction.Modify => permissions.CanModify,
            _ => false
        };
    }

    public async Task<EffectiveModulePermission> GetEffectivePermissionsAsync(
        PermissionModule module,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(module)
            || !currentUserSession.IsAuthenticated
            || currentUserSession.UserId is not long userId
            || currentUserSession.Role is not UserRole role)
        {
            return NoPermissions;
        }

        if (role == UserRole.Responsable)
        {
            return AllPermissions;
        }

        if (role != UserRole.Employe)
        {
            return NoPermissions;
        }

        var storedPermission = await dbContext.UserPermissions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                permission => permission.UserId == userId && permission.Module == module,
                cancellationToken);

        if (storedPermission is null || !storedPermission.CanView)
        {
            return NoPermissions;
        }

        return new EffectiveModulePermission(
            CanView: true,
            CanCreate: storedPermission.CanCreate,
            CanModify: storedPermission.CanModify);
    }

    public bool CanAccessResponsibleCapability(ResponsibleCapability capability) =>
        Enum.IsDefined(capability)
        && currentUserSession.IsAuthenticated
        && currentUserSession.Role == UserRole.Responsable;
}
