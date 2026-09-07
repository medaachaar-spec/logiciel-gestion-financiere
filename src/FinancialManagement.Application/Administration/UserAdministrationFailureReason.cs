namespace FinancialManagement.Application.Administration;

public enum UserAdministrationFailureReason
{
    None,
    NotAuthenticated,
    Forbidden,
    InvalidFullName,
    InvalidUsername,
    InvalidPassword,
    DuplicateUsername,
    UserNotFound,
    InvalidModule,
    EmployeeRequired,
    LastActiveResponsible,
    ConcurrentOperation
}
