namespace FinancialManagement.Application.Initialization;

public sealed class BootstrapResponsibleResult
{
    private BootstrapResponsibleResult(BootstrapResponsibleFailureReason failureReason, long? userId)
    {
        FailureReason = failureReason;
        UserId = userId;
    }

    public bool Success => FailureReason == BootstrapResponsibleFailureReason.None;
    public BootstrapResponsibleFailureReason FailureReason { get; }
    public long? UserId { get; }

    public static BootstrapResponsibleResult Succeeded(long userId) =>
        new(BootstrapResponsibleFailureReason.None, userId);
    public static BootstrapResponsibleResult Failed(BootstrapResponsibleFailureReason reason) => new(reason, null);
}
