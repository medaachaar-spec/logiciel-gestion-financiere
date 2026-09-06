namespace FinancialManagement.Application.Expenses;

public sealed class CreateExpenseResult
{
    private CreateExpenseResult(
        bool success,
        CreateExpenseFailureReason failureReason,
        long? expenseId,
        string? expenseReference,
        DateOnly? expenseDate,
        long amountMinor)
    {
        Success = success;
        FailureReason = failureReason;
        ExpenseId = expenseId;
        ExpenseReference = expenseReference;
        ExpenseDate = expenseDate;
        AmountMinor = amountMinor;
    }

    public bool Success { get; }

    public CreateExpenseFailureReason FailureReason { get; }

    public long? ExpenseId { get; }

    public string? ExpenseReference { get; }

    public DateOnly? ExpenseDate { get; }

    public long AmountMinor { get; }

    public static CreateExpenseResult Failed(
        CreateExpenseFailureReason failureReason,
        long amountMinor) =>
        new(false, failureReason, null, null, null, amountMinor);

    public static CreateExpenseResult Succeeded(
        long expenseId,
        string expenseReference,
        DateOnly expenseDate,
        long amountMinor) =>
        new(true, CreateExpenseFailureReason.None, expenseId, expenseReference, expenseDate, amountMinor);
}
