using FinancialManagement.Application.Administration;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Administration;

internal sealed class UserAdministrationService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession session,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider) : IUserAdministrationService
{
    public async Task<UserAdministrationListResult> GetUsersAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var failure = await AuthorizeAsync(cancellationToken);
        if (failure != UserAdministrationFailureReason.None)
            return UserAdministrationListResult.Failed(failure);

        var users = await dbContext.Users.AsNoTracking().OrderBy(user => user.Username).ThenBy(user => user.Id)
            .Select(user => new { user.Id, user.FullName, user.Username, user.Role, user.IsActive })
            .ToListAsync(cancellationToken);
        var permissions = await dbContext.UserPermissions.AsNoTracking().ToListAsync(cancellationToken);
        var items = users.Select(user => new UserAdministrationItem(
            user.Id, user.FullName, user.Username, user.Role, user.IsActive,
            Enum.GetValues<PermissionModule>().Select(module =>
            {
                var permission = permissions.SingleOrDefault(item => item.UserId == user.Id && item.Module == module);
                var canView = user.Role == UserRole.Employe && permission?.CanView == true;
                return new EmployeePermissionInput(module, canView,
                    canView && permission!.CanCreate, canView && permission!.CanModify);
            }).ToArray())).ToArray();
        await transaction.CommitAsync(cancellationToken);
        return UserAdministrationListResult.Succeeded(items);
    }

    public Task<UserAdministrationResult> CreateEmployeeAsync(
        CreateEmployeeCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return ExecuteWriteAsync(async () =>
        {
            if (string.IsNullOrWhiteSpace(command.FullName))
                return Failed(UserAdministrationFailureReason.InvalidFullName);
            if (string.IsNullOrWhiteSpace(command.Username))
                return Failed(UserAdministrationFailureReason.InvalidUsername);
            if (string.IsNullOrEmpty(command.Password))
                return Failed(UserAdministrationFailureReason.InvalidPassword);
            if (await dbContext.Users.AnyAsync(user => user.Username == command.Username, cancellationToken))
                return Failed(UserAdministrationFailureReason.DuplicateUsername);

            var now = timeProvider.GetUtcNow();
            var user = new User
            {
                FullName = command.FullName.Trim(), Username = command.Username,
                PasswordHash = passwordHasher.HashPassword(command.Password),
                Role = UserRole.Employe, IsActive = true, CreatedAtUtc = now, UpdatedAtUtc = now
            };
            try
            {
                dbContext.Users.Add(user);
                await dbContext.SaveChangesAsync(cancellationToken);
                return UserAdministrationResult.Succeeded(user.Id);
            }
            finally { dbContext.Entry(user).State = EntityState.Detached; }
        }, cancellationToken);
    }

    public Task<UserAdministrationResult> SetActiveAsync(
        long userId, bool isActive, CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(async () =>
        {
            var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
            if (user is null) return Failed(UserAdministrationFailureReason.UserNotFound);
            // Prevent administration from making an initialized installation inaccessible.
            if (!isActive && user.Role == UserRole.Responsable && user.IsActive
                && !await dbContext.Users.AnyAsync(item => item.Id != userId && item.IsActive
                    && item.Role == UserRole.Responsable, cancellationToken))
                return Failed(UserAdministrationFailureReason.LastActiveResponsible);

            var now = timeProvider.GetUtcNow();
            await dbContext.Users.Where(item => item.Id == userId).ExecuteUpdateAsync(setters =>
                setters.SetProperty(item => item.IsActive, isActive).SetProperty(item => item.UpdatedAtUtc, now), cancellationToken);
            return UserAdministrationResult.Succeeded(userId);
        }, cancellationToken);

    public Task<UserAdministrationResult> SetPermissionAsync(
        long userId, EmployeePermissionInput permission, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(permission);
        return ExecuteWriteAsync(async () =>
        {
            if (!Enum.IsDefined(permission.Module)) return Failed(UserAdministrationFailureReason.InvalidModule);
            var user = await dbContext.Users.AsNoTracking().SingleOrDefaultAsync(item => item.Id == userId, cancellationToken);
            if (user is null) return Failed(UserAdministrationFailureReason.UserNotFound);
            if (user.Role != UserRole.Employe) return Failed(UserAdministrationFailureReason.EmployeeRequired);
            var canCreate = permission.CanView && permission.CanCreate;
            var canModify = permission.CanView && permission.CanModify;
            var updated = await dbContext.UserPermissions.Where(item => item.UserId == userId && item.Module == permission.Module)
                .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.CanView, permission.CanView)
                    .SetProperty(item => item.CanCreate, canCreate).SetProperty(item => item.CanModify, canModify), cancellationToken);
            if (updated == 0)
            {
                var entity = new UserPermission
                {
                    UserId = userId, Module = permission.Module, CanView = permission.CanView,
                    CanCreate = canCreate, CanModify = canModify
                };
                try
                {
                    dbContext.UserPermissions.Add(entity);
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
                finally { dbContext.Entry(entity).State = EntityState.Detached; }
            }
            return UserAdministrationResult.Succeeded(userId);
        }, cancellationToken);
    }

    public Task<UserAdministrationResult> ResetPasswordAsync(
        long userId, string newPassword, CancellationToken cancellationToken = default) =>
        ExecuteWriteAsync(async () =>
        {
            if (string.IsNullOrEmpty(newPassword)) return Failed(UserAdministrationFailureReason.InvalidPassword);
            if (!await dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken))
                return Failed(UserAdministrationFailureReason.UserNotFound);
            var hash = passwordHasher.HashPassword(newPassword);
            var now = timeProvider.GetUtcNow();
            var updated = await dbContext.Users.Where(user => user.Id == userId).ExecuteUpdateAsync(setters =>
                setters.SetProperty(user => user.PasswordHash, hash).SetProperty(user => user.UpdatedAtUtc, now), cancellationToken);
            return updated == 1 ? UserAdministrationResult.Succeeded(userId) : Failed(UserAdministrationFailureReason.UserNotFound);
        }, cancellationToken);

    private async Task<UserAdministrationResult> ExecuteWriteAsync(
        Func<Task<UserAdministrationResult>> operation, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!session.IsAuthenticated || session.UserId is null)
            return Failed(UserAdministrationFailureReason.NotAuthenticated);
        var actorId = session.UserId;
        var connectionOpened = false;
        try
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            connectionOpened = true;
            var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var enlistment = await dbContext.Database.UseTransactionAsync(transaction, cancellationToken);
            // Re-read active role after writer-lock acquisition, never trust a stale session for administration.
            var failure = await AuthorizeAsync(cancellationToken);
            if (failure != UserAdministrationFailureReason.None) return Failed(failure);
            if (session.UserId != actorId) return Failed(UserAdministrationFailureReason.Forbidden);
            var result = await operation();
            if (result.Success)
            {
                failure = await AuthorizeAsync(cancellationToken);
                if (failure != UserAdministrationFailureReason.None) return Failed(failure);
                if (session.UserId != actorId) return Failed(UserAdministrationFailureReason.Forbidden);
                await transaction.CommitAsync(cancellationToken);
            }
            return result;
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failed(UserAdministrationFailureReason.ConcurrentOperation);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 5 or 6 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failed(UserAdministrationFailureReason.ConcurrentOperation);
        }
        finally
        {
            if (connectionOpened)
                await dbContext.Database.CloseConnectionAsync();
        }
    }

    private async Task<UserAdministrationFailureReason> AuthorizeAsync(CancellationToken cancellationToken)
    {
        if (!session.IsAuthenticated || session.UserId is not long userId)
            return UserAdministrationFailureReason.NotAuthenticated;
        return await dbContext.Users.AsNoTracking().AnyAsync(user => user.Id == userId
            && user.IsActive && user.Role == UserRole.Responsable, cancellationToken)
            ? UserAdministrationFailureReason.None : UserAdministrationFailureReason.Forbidden;
    }

    private static UserAdministrationResult Failed(UserAdministrationFailureReason reason) => UserAdministrationResult.Failed(reason);
}
