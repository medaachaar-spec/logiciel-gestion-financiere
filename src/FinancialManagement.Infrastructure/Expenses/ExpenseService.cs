using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Expenses;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Domain;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Expenses;

internal sealed class ExpenseService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    IBusinessReferenceGenerator referenceGenerator,
    TimeProvider timeProvider) : IExpenseService
{
    public async Task<CreateExpenseResult> CreateAsync(
        CreateExpenseCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is not long userId)
        {
            return CreateExpenseResult.Failed(
                CreateExpenseFailureReason.NotAuthenticated,
                command.AmountMinor);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Expenses,
                PermissionAction.Create,
                cancellationToken))
        {
            return CreateExpenseResult.Failed(
                CreateExpenseFailureReason.NotAuthorized,
                command.AmountMinor);
        }

        if (command.AmountMinor <= 0)
        {
            return CreateExpenseResult.Failed(
                CreateExpenseFailureReason.InvalidAmount,
                command.AmountMinor);
        }

        if (!Enum.IsDefined(command.PaymentMethod))
        {
            return CreateExpenseResult.Failed(
                CreateExpenseFailureReason.InvalidPaymentMethod,
                command.AmountMinor);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await authorizationService.CanAsync(
                PermissionModule.Expenses,
                PermissionAction.Create,
                cancellationToken)
            || currentUserSession.UserId != userId)
        {
            return CreateExpenseResult.Failed(
                CreateExpenseFailureReason.NotAuthorized,
                command.AmountMinor);
        }

        var category = await dbContext.ExpenseCategories
            .AsNoTracking()
            .Where(item => item.Id == command.ExpenseCategoryId)
            .Select(item => new { item.IsActive })
            .SingleOrDefaultAsync(cancellationToken);

        if (category is null)
        {
            return CreateExpenseResult.Failed(
                CreateExpenseFailureReason.ExpenseCategoryNotFound,
                command.AmountMinor);
        }

        if (!category.IsActive)
        {
            return CreateExpenseResult.Failed(
                CreateExpenseFailureReason.ExpenseCategoryInactive,
                command.AmountMinor);
        }

        if (command.ExpenseCategoryId == ExpenseCategoryDefaults.SalaryId
            && command.ConcernedEmployeeUserId is null)
        {
            return CreateExpenseResult.Failed(
                CreateExpenseFailureReason.ConcernedEmployeeRequired,
                command.AmountMinor);
        }

        if (command.ConcernedEmployeeUserId is long concernedEmployeeUserId)
        {
            var employee = await dbContext.Users
                .AsNoTracking()
                .Where(item => item.Id == concernedEmployeeUserId)
                .Select(item => new { item.IsActive, item.Role })
                .SingleOrDefaultAsync(cancellationToken);

            if (employee is null)
            {
                return CreateExpenseResult.Failed(
                    CreateExpenseFailureReason.ConcernedEmployeeNotFound,
                    command.AmountMinor);
            }

            if (!employee.IsActive || employee.Role != UserRole.Employe)
            {
                return CreateExpenseResult.Failed(
                    CreateExpenseFailureReason.ConcernedEmployeeNotEligible,
                    command.AmountMinor);
            }
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var localDateTime = TimeZoneInfo.ConvertTime(createdAtUtc, timeProvider.LocalTimeZone);
        var expenseDate = DateOnly.FromDateTime(localDateTime.DateTime);
        var expense = new Expense
        {
            Reference = referenceGenerator.GenerateExpenseReference(expenseDate),
            ExpenseDate = expenseDate,
            ExpenseCategoryId = command.ExpenseCategoryId,
            AmountMinor = command.AmountMinor,
            PaymentMethod = command.PaymentMethod,
            ConcernedEmployeeUserId = command.ConcernedEmployeeUserId,
            Observation = command.Observation,
            CreatedByUserId = userId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

        dbContext.Expenses.Add(expense);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateExpenseResult.Succeeded(
            expense.Id,
            expense.Reference,
            expense.ExpenseDate,
            expense.AmountMinor);
    }

    public async Task<ExpenseListResult> GetPageAsync(
        ExpenseListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return ExpenseListResult.Failed(query, ExpenseListFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Expenses,
                PermissionAction.View,
                cancellationToken))
        {
            return ExpenseListResult.Failed(query, ExpenseListFailureReason.Forbidden);
        }

        if (query.PageNumber < 1)
        {
            return ExpenseListResult.Failed(query, ExpenseListFailureReason.InvalidPageNumber);
        }

        if (query.PageSize is < 1 or > 200)
        {
            return ExpenseListResult.Failed(query, ExpenseListFailureReason.InvalidPageSize);
        }

        if (query.FromDate is DateOnly fromDate
            && query.ToDate is DateOnly toDate
            && fromDate > toDate)
        {
            return ExpenseListResult.Failed(query, ExpenseListFailureReason.InvalidDateRange);
        }

        var skip = ((long)query.PageNumber - 1) * query.PageSize;
        if (skip > int.MaxValue)
        {
            return ExpenseListResult.Failed(query, ExpenseListFailureReason.InvalidPageNumber);
        }

        var expenses = BuildReadQuery();
        if (query.FromDate is DateOnly inclusiveFromDate)
        {
            expenses = expenses.Where(expense => expense.ExpenseDate >= inclusiveFromDate);
        }

        if (query.ToDate is DateOnly inclusiveToDate)
        {
            expenses = expenses.Where(expense => expense.ExpenseDate <= inclusiveToDate);
        }

        var searchText = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(searchText))
        {
            expenses = expenses.Where(expense =>
                expense.Reference.Contains(searchText)
                || expense.ExpenseCategoryName.Contains(searchText)
                || (expense.ConcernedEmployeeDisplayName != null
                    && expense.ConcernedEmployeeDisplayName.Contains(searchText)));
        }

        var totalCount = await expenses.CountAsync(cancellationToken);
        var items = await expenses
            .OrderByDescending(expense => expense.ExpenseDate)
            .ThenByDescending(expense => expense.Id)
            .Skip((int)skip)
            .Take(query.PageSize)
            .Select(expense => new ExpenseListItem(
                expense.Id,
                expense.Reference,
                expense.ExpenseDate,
                expense.ExpenseCategoryId,
                expense.ExpenseCategoryName,
                expense.AmountMinor,
                expense.PaymentMethod,
                expense.ConcernedEmployeeUserId,
                expense.ConcernedEmployeeDisplayName,
                expense.Observation))
            .ToListAsync(cancellationToken);

        return ExpenseListResult.Succeeded(query, items, totalCount);
    }

    public async Task<ExpenseDetailResult> GetByIdAsync(
        long expenseId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return ExpenseDetailResult.Failed(ExpenseDetailFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Expenses,
                PermissionAction.View,
                cancellationToken))
        {
            return ExpenseDetailResult.Failed(ExpenseDetailFailureReason.Forbidden);
        }

        if (expenseId <= 0)
        {
            return ExpenseDetailResult.Failed(ExpenseDetailFailureReason.InvalidExpenseId);
        }

        var item = await BuildReadQuery()
            .Where(expense => expense.Id == expenseId)
            .Select(expense => new ExpenseDetailItem(
                expense.Id,
                expense.Reference,
                expense.ExpenseDate,
                expense.ExpenseCategoryId,
                expense.ExpenseCategoryName,
                expense.AmountMinor,
                expense.PaymentMethod,
                expense.ConcernedEmployeeUserId,
                expense.ConcernedEmployeeDisplayName,
                expense.Observation))
            .SingleOrDefaultAsync(cancellationToken);

        return item is null
            ? ExpenseDetailResult.Failed(ExpenseDetailFailureReason.NotFound)
            : ExpenseDetailResult.Succeeded(item);
    }

    private IQueryable<ExpenseReadProjection> BuildReadQuery() =>
        from expense in dbContext.Expenses.AsNoTracking()
        join category in dbContext.ExpenseCategories.AsNoTracking()
            on expense.ExpenseCategoryId equals category.Id
        join employee in dbContext.Users.AsNoTracking()
            on expense.ConcernedEmployeeUserId equals (long?)employee.Id into employees
        from employee in employees.DefaultIfEmpty()
        select new ExpenseReadProjection
        {
            Id = expense.Id,
            Reference = expense.Reference,
            ExpenseDate = expense.ExpenseDate,
            ExpenseCategoryId = expense.ExpenseCategoryId,
            ExpenseCategoryName = category.Name,
            AmountMinor = expense.AmountMinor,
            PaymentMethod = expense.PaymentMethod,
            ConcernedEmployeeUserId = expense.ConcernedEmployeeUserId,
            ConcernedEmployeeDisplayName = employee == null ? null : employee.FullName,
            Observation = expense.Observation
        };

    private sealed class ExpenseReadProjection
    {
        public long Id { get; init; }

        public required string Reference { get; init; }

        public DateOnly ExpenseDate { get; init; }

        public long ExpenseCategoryId { get; init; }

        public required string ExpenseCategoryName { get; init; }

        public long AmountMinor { get; init; }

        public PaymentMethod PaymentMethod { get; init; }

        public long? ConcernedEmployeeUserId { get; init; }

        public string? ConcernedEmployeeDisplayName { get; init; }

        public string? Observation { get; init; }
    }
}
