namespace FinancialManagement.Domain.Entities;

public sealed class ExpenseCategory
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }

    public ICollection<Expense> Expenses { get; set; } = [];
}
