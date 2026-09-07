namespace FinancialManagement.Application.Administration;

public interface IUserAdministrationService
{
    Task<UserAdministrationListResult> GetUsersAsync(CancellationToken cancellationToken = default);
    Task<UserAdministrationResult> CreateEmployeeAsync(CreateEmployeeCommand command, CancellationToken cancellationToken = default);
    Task<UserAdministrationResult> SetActiveAsync(long userId, bool isActive, CancellationToken cancellationToken = default);
    Task<UserAdministrationResult> SetPermissionAsync(
        long userId, EmployeePermissionInput permission, CancellationToken cancellationToken = default);
    Task<UserAdministrationResult> ResetPasswordAsync(long userId, string newPassword, CancellationToken cancellationToken = default);
}
