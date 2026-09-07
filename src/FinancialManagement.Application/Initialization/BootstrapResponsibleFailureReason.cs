namespace FinancialManagement.Application.Initialization;

public enum BootstrapResponsibleFailureReason
{
    None,
    BootstrapUnavailable,
    InvalidFullName,
    InvalidUsername,
    InvalidPassword,
    ConcurrentOperation
}
