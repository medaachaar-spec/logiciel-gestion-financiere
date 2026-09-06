namespace FinancialManagement.Application.Employees;

public sealed class EmployeeLookupResult
{
    private EmployeeLookupResult(
        bool success,
        EmployeeLookupFailureReason failureReason,
        IReadOnlyList<EmployeeLookupItem> items,
        int limit)
    {
        Success = success;
        FailureReason = failureReason;
        Items = items;
        Limit = limit;
    }

    public bool Success { get; }

    public EmployeeLookupFailureReason FailureReason { get; }

    public IReadOnlyList<EmployeeLookupItem> Items { get; }

    public int Limit { get; }

    public static EmployeeLookupResult Failed(
        EmployeeLookupQuery query,
        EmployeeLookupFailureReason failureReason) =>
        new(false, failureReason, Array.Empty<EmployeeLookupItem>(), query.Limit);

    public static EmployeeLookupResult Succeeded(
        EmployeeLookupQuery query,
        IReadOnlyList<EmployeeLookupItem> items) =>
        new(true, EmployeeLookupFailureReason.None, items, query.Limit);
}
