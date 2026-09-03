using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Domain.Entities;

public sealed class Expense
{
    public long Id { get; set; }
    public string Reference { get; set; } = string.Empty;
    public DateOnly ExpenseDate { get; set; }
    public long ExpenseCategoryId { get; set; }
    public long AmountMinor { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public long? ConcernedEmployeeUserId { get; set; }
    public string? Observation { get; set; }
    public long CreatedByUserId { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset UpdatedAtUtc { get; set; }
}
