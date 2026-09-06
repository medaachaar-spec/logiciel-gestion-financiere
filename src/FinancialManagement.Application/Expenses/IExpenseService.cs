namespace FinancialManagement.Application.Expenses;

public interface IExpenseService
{
    Task<CreateExpenseResult> CreateAsync(
        CreateExpenseCommand command,
        CancellationToken cancellationToken = default);

    Task<ExpenseListResult> GetPageAsync(
        ExpenseListQuery query,
        CancellationToken cancellationToken = default);

    Task<ExpenseDetailResult> GetByIdAsync(
        long expenseId,
        CancellationToken cancellationToken = default);
}
