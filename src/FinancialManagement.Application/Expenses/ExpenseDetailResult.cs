namespace FinancialManagement.Application.Expenses;

public sealed class ExpenseDetailResult
{
    private ExpenseDetailResult(
        bool success,
        ExpenseDetailFailureReason failureReason,
        ExpenseDetailItem? item)
    {
        Success = success;
        FailureReason = failureReason;
        Item = item;
    }

    public bool Success { get; }

    public ExpenseDetailFailureReason FailureReason { get; }

    public ExpenseDetailItem? Item { get; }

    public static ExpenseDetailResult Failed(ExpenseDetailFailureReason failureReason) =>
        new(false, failureReason, null);

    public static ExpenseDetailResult Succeeded(ExpenseDetailItem item) =>
        new(true, ExpenseDetailFailureReason.None, item);
}
