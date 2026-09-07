namespace FinancialManagement.Application.Administration;

public sealed class UserAdministrationListResult
{
    private UserAdministrationListResult(UserAdministrationFailureReason reason, IReadOnlyList<UserAdministrationItem> items)
    {
        FailureReason = reason;
        Items = items;
    }

    public bool Success => FailureReason == UserAdministrationFailureReason.None;
    public UserAdministrationFailureReason FailureReason { get; }
    public IReadOnlyList<UserAdministrationItem> Items { get; }

    public static UserAdministrationListResult Succeeded(IReadOnlyList<UserAdministrationItem> items) =>
        new(UserAdministrationFailureReason.None, items);
    public static UserAdministrationListResult Failed(UserAdministrationFailureReason reason) => new(reason, []);
}
