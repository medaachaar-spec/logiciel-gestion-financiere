namespace FinancialManagement.Application.Administration;

public sealed class UserAdministrationResult
{
    private UserAdministrationResult(UserAdministrationFailureReason failureReason, long? userId)
    {
        FailureReason = failureReason;
        UserId = userId;
    }

    public bool Success => FailureReason == UserAdministrationFailureReason.None;
    public UserAdministrationFailureReason FailureReason { get; }
    public long? UserId { get; }

    public static UserAdministrationResult Succeeded(long userId) => new(UserAdministrationFailureReason.None, userId);
    public static UserAdministrationResult Failed(UserAdministrationFailureReason reason) => new(reason, null);
}
