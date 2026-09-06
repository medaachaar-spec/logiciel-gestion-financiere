using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.Expenses;

public sealed record ExpenseDetailItem(
    long Id,
    string Reference,
    DateOnly ExpenseDate,
    long ExpenseCategoryId,
    string ExpenseCategoryName,
    long AmountMinor,
    PaymentMethod PaymentMethod,
    long? ConcernedEmployeeUserId,
    string? ConcernedEmployeeDisplayName,
    string? Observation);
