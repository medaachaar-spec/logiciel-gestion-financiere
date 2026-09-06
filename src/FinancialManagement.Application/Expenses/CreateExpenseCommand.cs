using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.Expenses;

public sealed record CreateExpenseCommand(
    long ExpenseCategoryId,
    long AmountMinor,
    PaymentMethod PaymentMethod,
    long? ConcernedEmployeeUserId,
    string? Observation);
