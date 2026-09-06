using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Dashboard;
using FinancialManagement.Application.Employees;
using FinancialManagement.Application.ExpenseCategories;
using FinancialManagement.Application.Expenses;
using FinancialManagement.Application.Sales;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class ExpenseCreateServiceTests
{
    [Fact]
    public async Task Responsable_creates_expense_with_backend_generated_fields()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("expense-responsible", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.CreateAsync(
            new(1, 125_075, PaymentMethod.Cash, null, "Office expense"));

        Assert.True(result.Success);
        Assert.Equal(CreateExpenseFailureReason.None, result.FailureReason);
        Assert.Equal(ExpenseTestDatabase.BusinessDate, result.ExpenseDate);
        Assert.Equal(125_075, result.AmountMinor);
        Assert.Matches("^DEP-20260907-[0-9A-F]{12}$", result.ExpenseReference);
        var expense = Assert.Single(database.Context.Expenses);
        Assert.Equal(result.ExpenseId, expense.Id);
        Assert.Equal(result.ExpenseReference, expense.Reference);
        Assert.Equal(responsible.Id, expense.CreatedByUserId);
        Assert.Equal(ExpenseTestDatabase.TestUtc, expense.CreatedAtUtc);
        Assert.Equal(ExpenseTestDatabase.TestUtc, expense.UpdatedAtUtc);
        Assert.Equal("Office expense", expense.Observation);
    }

    [Fact]
    public async Task Employee_with_expenses_create_permission_can_create()
    {
        using var database = new ExpenseTestDatabase();
        var employee = database.AddUser("expense-creator", UserRole.Employe);
        database.GrantExpenses(employee, canView: true, canCreate: true);
        await database.AuthenticateAsync(employee);

        var result = await database.ExpenseService.CreateAsync(
            new(2, 500, PaymentMethod.BankTransfer, null, null));

        Assert.True(result.Success);
        Assert.Equal(employee.Id, Assert.Single(database.Context.Expenses).CreatedByUserId);
    }

    [Fact]
    public async Task Employee_without_create_permission_is_rejected()
    {
        using var database = new ExpenseTestDatabase();
        var employee = database.AddUser("expense-denied", UserRole.Employe);
        database.GrantExpenses(employee, canView: true, canCreate: false);
        await database.AuthenticateAsync(employee);

        var result = await database.ExpenseService.CreateAsync(
            new(1, 500, PaymentMethod.Cash, null, null));

        Assert.False(result.Success);
        Assert.Equal(CreateExpenseFailureReason.NotAuthorized, result.FailureReason);
        Assert.Empty(database.Context.Expenses);
    }

    [Fact]
    public async Task Unauthenticated_create_is_rejected()
    {
        using var database = new ExpenseTestDatabase();

        var result = await database.ExpenseService.CreateAsync(
            new(1, 500, PaymentMethod.Cash, null, null));

        Assert.False(result.Success);
        Assert.Equal(CreateExpenseFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(database.Context.Expenses);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_amount_is_rejected(long amountMinor)
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser($"invalid-amount-{amountMinor}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.CreateAsync(
            new(1, amountMinor, PaymentMethod.Cash, null, null));

        Assert.False(result.Success);
        Assert.Equal(CreateExpenseFailureReason.InvalidAmount, result.FailureReason);
        Assert.Empty(database.Context.Expenses);
    }

    [Fact]
    public async Task Long_max_value_is_preserved_without_floating_point_conversion()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("long-amount", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.CreateAsync(
            new(1, long.MaxValue, PaymentMethod.Cash, null, null));

        Assert.True(result.Success);
        Assert.Equal(long.MaxValue, result.AmountMinor);
        Assert.Equal(long.MaxValue, Assert.Single(database.Context.Expenses).AmountMinor);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.BankTransfer)]
    public async Task Supported_payment_methods_are_accepted(PaymentMethod paymentMethod)
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser($"method-{paymentMethod}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.CreateAsync(
            new(1, 100, paymentMethod, null, null));

        Assert.True(result.Success);
        Assert.Equal(paymentMethod, Assert.Single(database.Context.Expenses).PaymentMethod);
    }

    [Fact]
    public async Task Undefined_payment_method_is_rejected()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("invalid-method", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.CreateAsync(
            new(1, 100, (PaymentMethod)999, null, null));

        Assert.False(result.Success);
        Assert.Equal(CreateExpenseFailureReason.InvalidPaymentMethod, result.FailureReason);
        Assert.Empty(database.Context.Expenses);
    }

    [Fact]
    public async Task Missing_category_is_rejected()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("missing-category", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.CreateAsync(
            new(999, 100, PaymentMethod.Cash, null, null));

        Assert.False(result.Success);
        Assert.Equal(CreateExpenseFailureReason.ExpenseCategoryNotFound, result.FailureReason);
        Assert.Empty(database.Context.Expenses);
    }

    [Fact]
    public async Task Inactive_category_is_rejected()
    {
        using var database = new ExpenseTestDatabase();
        database.SetCategoryActive(1, false);
        var responsible = database.AddUser("inactive-category", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.CreateAsync(
            new(1, 100, PaymentMethod.Cash, null, null));

        Assert.False(result.Success);
        Assert.Equal(CreateExpenseFailureReason.ExpenseCategoryInactive, result.FailureReason);
        Assert.Empty(database.Context.Expenses);
    }

    [Fact]
    public async Task Active_employee_can_be_concerned()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("employee-selector", UserRole.Responsable);
        var employee = database.AddUser("concerned-active", UserRole.Employe);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.CreateAsync(
            new(1, 100, PaymentMethod.Cash, employee.Id, null));

        Assert.True(result.Success);
        Assert.Equal(employee.Id, Assert.Single(database.Context.Expenses).ConcernedEmployeeUserId);
    }

    [Fact]
    public async Task Missing_concerned_employee_is_rejected()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("employee-missing", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.CreateAsync(
            new(1, 100, PaymentMethod.Cash, 999, null));

        Assert.False(result.Success);
        Assert.Equal(CreateExpenseFailureReason.ConcernedEmployeeNotFound, result.FailureReason);
        Assert.Empty(database.Context.Expenses);
    }

    [Theory]
    [InlineData(UserRole.Employe, false)]
    [InlineData(UserRole.Responsable, true)]
    public async Task Ineligible_concerned_account_is_rejected(UserRole role, bool isActive)
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser($"selector-{role}-{isActive}", UserRole.Responsable);
        var concerned = database.AddUser($"ineligible-{role}-{isActive}", role, isActive);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.CreateAsync(
            new(1, 100, PaymentMethod.Cash, concerned.Id, null));

        Assert.False(result.Success);
        Assert.Equal(CreateExpenseFailureReason.ConcernedEmployeeNotEligible, result.FailureReason);
        Assert.Empty(database.Context.Expenses);
    }

    [Fact]
    public async Task Null_observation_and_no_concerned_employee_are_accepted()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("optional-fields", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.CreateAsync(
            new(1, 100, PaymentMethod.Cash, null, null));

        Assert.True(result.Success);
        var expense = Assert.Single(database.Context.Expenses);
        Assert.Null(expense.Observation);
        Assert.Null(expense.ConcernedEmployeeUserId);
    }

    [Fact]
    public async Task Two_creates_generate_two_unique_references()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("unique-references", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var first = await database.ExpenseService.CreateAsync(
            new(1, 100, PaymentMethod.Cash, null, null));
        var second = await database.ExpenseService.CreateAsync(
            new(1, 200, PaymentMethod.Cash, null, null));

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.NotEqual(first.ExpenseReference, second.ExpenseReference);
        Assert.Equal(2, database.Context.Expenses.Select(item => item.Reference).Distinct().Count());
    }

    [Fact]
    public void Production_reference_generator_uses_DEP_date_and_unique_hex_suffix()
    {
        using var serviceProvider = new ServiceCollection()
            .AddInfrastructure("Data Source=:memory:")
            .BuildServiceProvider();
        var generator = serviceProvider.GetRequiredService<IBusinessReferenceGenerator>();

        var first = generator.GenerateExpenseReference(ExpenseTestDatabase.BusinessDate);
        var second = generator.GenerateExpenseReference(ExpenseTestDatabase.BusinessDate);

        Assert.Matches("^DEP-20260907-[0-9A-F]{12}$", first);
        Assert.Matches("^DEP-20260907-[0-9A-F]{12}$", second);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Cancellation_is_propagated_before_work()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("cancel-create", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.ExpenseService.CreateAsync(
                new(1, 100, PaymentMethod.Cash, null, null),
                source.Token));

        Assert.Empty(database.Context.Expenses);
    }
}

public sealed class ExpenseReadServiceTests
{
    [Fact]
    public async Task View_permission_returns_all_users_expenses_with_exact_projection()
    {
        using var database = new ExpenseTestDatabase();
        var creator = database.AddUser("read-owner", UserRole.Responsable);
        var reader = database.AddUser("read-employee", UserRole.Employe);
        var concerned = database.AddUser("read-concerned", UserRole.Employe, fullName: "Yasmine Employee");
        database.GrantExpenses(reader, canView: true, canCreate: false);
        var expense = database.AddExpense(
            creator,
            "DEP-READ-001",
            new(2026, 9, 5),
            1,
            9_000_000_000,
            PaymentMethod.BankTransfer,
            concerned.Id,
            "Read observation");
        await database.AuthenticateAsync(reader);
        database.Context.ChangeTracker.Clear();

        var result = await database.ExpenseService.GetPageAsync(new());

        Assert.True(result.Success);
        var item = Assert.Single(result.Items);
        Assert.Equal(expense.Id, item.Id);
        Assert.Equal("DEP-READ-001", item.Reference);
        Assert.Equal(new DateOnly(2026, 9, 5), item.ExpenseDate);
        Assert.Equal(1, item.ExpenseCategoryId);
        Assert.Equal("Salaires", item.ExpenseCategoryName);
        Assert.Equal(9_000_000_000, item.AmountMinor);
        Assert.Equal(PaymentMethod.BankTransfer, item.PaymentMethod);
        Assert.Equal(concerned.Id, item.ConcernedEmployeeUserId);
        Assert.Equal("Yasmine Employee", item.ConcernedEmployeeDisplayName);
        Assert.Equal("Read observation", item.Observation);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Unauthenticated_list_is_rejected_without_data()
    {
        using var database = new ExpenseTestDatabase();
        var creator = database.AddUser("list-secret-owner", UserRole.Responsable);
        database.AddExpense(creator, "DEP-SECRET", new(2026, 9, 1));

        var result = await database.ExpenseService.GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(ExpenseListFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Create_permission_does_not_bypass_missing_view_permission()
    {
        using var database = new ExpenseTestDatabase();
        var creator = database.AddUser("list-owner", UserRole.Responsable);
        var employee = database.AddUser("create-only", UserRole.Employe);
        database.GrantExpenses(employee, canView: false, canCreate: true);
        database.AddExpense(creator, "DEP-NO-VIEW", new(2026, 9, 1));
        await database.AuthenticateAsync(employee);

        var result = await database.ExpenseService.GetPageAsync(new());

        Assert.False(result.Success);
        Assert.Equal(ExpenseListFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Theory]
    [InlineData(0, 50, ExpenseListFailureReason.InvalidPageNumber)]
    [InlineData(1, 0, ExpenseListFailureReason.InvalidPageSize)]
    [InlineData(1, 201, ExpenseListFailureReason.InvalidPageSize)]
    [InlineData(int.MaxValue, 200, ExpenseListFailureReason.InvalidPageNumber)]
    public async Task Invalid_pagination_is_rejected(
        int pageNumber,
        int pageSize,
        ExpenseListFailureReason expected)
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser($"paging-{pageNumber}-{pageSize}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.GetPageAsync(
            new(pageNumber, pageSize, null, null, null));

        Assert.False(result.Success);
        Assert.Equal(expected, result.FailureReason);
    }

    [Fact]
    public async Task Pagination_is_stable_without_duplicates_or_gaps()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("stable-pages", UserRole.Responsable);
        var sameDate = new DateOnly(2026, 9, 2);
        var expected = Enumerable.Range(1, 5)
            .Select(index => database.AddExpense(responsible, $"DEP-PAGE-{index}", sameDate).Id)
            .OrderDescending()
            .ToArray();
        await database.AuthenticateAsync(responsible);

        var first = await database.ExpenseService.GetPageAsync(new(1, 2, null, null, null));
        var second = await database.ExpenseService.GetPageAsync(new(2, 2, null, null, null));
        var third = await database.ExpenseService.GetPageAsync(new(3, 2, null, null, null));
        var actual = first.Items.Concat(second.Items).Concat(third.Items).Select(item => item.Id).ToArray();

        Assert.Equal(5, first.TotalCount);
        Assert.Equal(3, first.TotalPages);
        Assert.Equal(expected, actual);
        Assert.Equal(5, actual.Distinct().Count());
    }

    [Fact]
    public async Task Date_filters_are_inclusive_and_invalid_ranges_are_rejected()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("date-filters", UserRole.Responsable);
        database.AddExpense(responsible, "DEP-BEFORE", new(2026, 8, 31));
        database.AddExpense(responsible, "DEP-FROM", new(2026, 9, 1));
        database.AddExpense(responsible, "DEP-TO", new(2026, 9, 30));
        database.AddExpense(responsible, "DEP-AFTER", new(2026, 10, 1));
        await database.AuthenticateAsync(responsible);

        var interval = await database.ExpenseService.GetPageAsync(
            new(1, 50, new(2026, 9, 1), new(2026, 9, 30), null));
        var sameDay = await database.ExpenseService.GetPageAsync(
            new(1, 50, new(2026, 9, 1), new(2026, 9, 1), null));
        var invalid = await database.ExpenseService.GetPageAsync(
            new(1, 50, new(2026, 9, 30), new(2026, 9, 1), null));

        Assert.Equal(new[] { "DEP-TO", "DEP-FROM" }, interval.Items.Select(item => item.Reference));
        Assert.Equal("DEP-FROM", Assert.Single(sameDay.Items).Reference);
        Assert.False(invalid.Success);
        Assert.Equal(ExpenseListFailureReason.InvalidDateRange, invalid.FailureReason);
    }

    [Fact]
    public async Task Search_matches_reference_category_and_employee_after_trim()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("search-owner", UserRole.Responsable);
        var employee = database.AddUser("search-employee", UserRole.Employe, fullName: "Nadia Atlas");
        database.AddExpense(responsible, "DEP-REF-ALPHA", new(2026, 9, 1), 1);
        database.AddExpense(responsible, "DEP-REF-BETA", new(2026, 9, 3), 1);
        database.AddExpense(responsible, "DEP-OTHER", new(2026, 9, 2), 2, concernedEmployeeUserId: employee.Id);
        await database.AuthenticateAsync(responsible);

        var byReference = await database.ExpenseService.GetPageAsync(
            new(1, 50, null, null, "  REF-ALPHA  "));
        var byCategory = await database.ExpenseService.GetPageAsync(
            new(1, 50, null, null, "Salaires"));
        var byEmployee = await database.ExpenseService.GetPageAsync(
            new(1, 50, null, null, "Nadia"));
        var whitespace = await database.ExpenseService.GetPageAsync(
            new(1, 50, null, null, "   "));
        var none = await database.ExpenseService.GetPageAsync(
            new(1, 50, null, null, "missing"));

        Assert.Equal("DEP-REF-ALPHA", Assert.Single(byReference.Items).Reference);
        Assert.Equal(new[] { "DEP-REF-BETA", "DEP-REF-ALPHA" }, byCategory.Items.Select(item => item.Reference));
        Assert.Equal("DEP-OTHER", Assert.Single(byEmployee.Items).Reference);
        Assert.Equal(3, whitespace.TotalCount);
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task Historical_inactive_category_and_employee_remain_visible()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("historical-owner", UserRole.Responsable);
        var employee = database.AddUser("historical-employee", UserRole.Employe, fullName: "Historical Employee");
        database.AddExpense(
            responsible,
            "DEP-HISTORICAL",
            new(2026, 9, 1),
            1,
            concernedEmployeeUserId: employee.Id);
        database.SetCategoryActive(1, false);
        database.SetUserActive(employee.Id, false);
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.ExpenseService.GetPageAsync(new());

        var item = Assert.Single(result.Items);
        Assert.Equal("Salaires", item.ExpenseCategoryName);
        Assert.Equal("Historical Employee", item.ConcernedEmployeeDisplayName);
    }

    [Fact]
    public async Task List_cancellation_is_propagated()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("cancel-list", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.ExpenseService.GetPageAsync(new(), source.Token));
    }
}

public sealed class ExpenseDetailServiceTests
{
    [Fact]
    public async Task Detail_returns_minimal_historical_projection()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("detail-owner", UserRole.Responsable);
        var employee = database.AddUser("detail-employee", UserRole.Employe, fullName: "Detail Employee");
        var expense = database.AddExpense(
            responsible,
            "DEP-DETAIL",
            new(2026, 9, 3),
            3,
            420,
            PaymentMethod.Cash,
            employee.Id,
            "Detail observation");
        database.SetCategoryActive(3, false);
        database.SetUserActive(employee.Id, false);
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.ExpenseService.GetByIdAsync(expense.Id);

        Assert.True(result.Success);
        Assert.NotNull(result.Item);
        Assert.Equal("DEP-DETAIL", result.Item.Reference);
        Assert.Equal("Loyer", result.Item.ExpenseCategoryName);
        Assert.Equal("Detail Employee", result.Item.ConcernedEmployeeDisplayName);
        Assert.Equal(420, result.Item.AmountMinor);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Non_positive_detail_id_is_rejected(long expenseId)
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser($"invalid-detail-{expenseId}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.GetByIdAsync(expenseId);

        Assert.False(result.Success);
        Assert.Equal(ExpenseDetailFailureReason.InvalidExpenseId, result.FailureReason);
    }

    [Fact]
    public async Task Missing_detail_is_controlled()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("missing-detail", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.ExpenseService.GetByIdAsync(999);

        Assert.False(result.Success);
        Assert.Equal(ExpenseDetailFailureReason.NotFound, result.FailureReason);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task Detail_requires_authentication_and_view_permission()
    {
        using var database = new ExpenseTestDatabase();
        var creator = database.AddUser("detail-secret-owner", UserRole.Responsable);
        var employee = database.AddUser("detail-create-only", UserRole.Employe);
        database.GrantExpenses(employee, canView: false, canCreate: true);
        var expense = database.AddExpense(creator, "DEP-DETAIL-SECRET", new(2026, 9, 1));

        var unauthenticated = await database.ExpenseService.GetByIdAsync(expense.Id);
        await database.AuthenticateAsync(employee);
        var forbidden = await database.ExpenseService.GetByIdAsync(expense.Id);

        Assert.Equal(ExpenseDetailFailureReason.NotAuthenticated, unauthenticated.FailureReason);
        Assert.Equal(ExpenseDetailFailureReason.Forbidden, forbidden.FailureReason);
        Assert.Null(unauthenticated.Item);
        Assert.Null(forbidden.Item);
    }
}

public sealed class ExpenseCategoryLookupServiceTests
{
    [Fact]
    public async Task Create_permission_returns_only_active_categories_in_name_order()
    {
        using var database = new ExpenseTestDatabase();
        var employee = database.AddUser("category-lookup", UserRole.Employe);
        database.GrantExpenses(employee, canView: true, canCreate: true);
        database.SetCategoryActive(2, false);
        await database.AuthenticateAsync(employee);
        database.Context.ChangeTracker.Clear();

        var result = await database.CategoryLookupService.GetActiveAsync();

        Assert.True(result.Success);
        Assert.Equal(4, result.Items.Count);
        Assert.DoesNotContain(result.Items, item => item.DisplayName == "Transport");
        Assert.Equal(
            result.Items.OrderBy(item => item.DisplayName).ThenBy(item => item.ExpenseCategoryId),
            result.Items);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Category_lookup_requires_authentication()
    {
        using var database = new ExpenseTestDatabase();

        var result = await database.CategoryLookupService.GetActiveAsync();

        Assert.False(result.Success);
        Assert.Equal(ExpenseCategoryLookupFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task View_without_create_cannot_use_category_lookup()
    {
        using var database = new ExpenseTestDatabase();
        var employee = database.AddUser("category-view-only", UserRole.Employe);
        database.GrantExpenses(employee, canView: true, canCreate: false);
        await database.AuthenticateAsync(employee);

        var result = await database.CategoryLookupService.GetActiveAsync();

        Assert.False(result.Success);
        Assert.Equal(ExpenseCategoryLookupFailureReason.Forbidden, result.FailureReason);
        Assert.Empty(result.Items);
    }
}

public sealed class EmployeeLookupServiceTests
{
    [Fact]
    public async Task Create_permission_returns_only_active_employees_in_deterministic_order()
    {
        using var database = new ExpenseTestDatabase();
        var creator = database.AddUser("employee-lookup-creator", UserRole.Employe, fullName: "Creator User");
        database.GrantExpenses(creator, canView: true, canCreate: true);
        var zoe = database.AddUser("zoe", UserRole.Employe, fullName: "Zoe Employee");
        var alice = database.AddUser("alice", UserRole.Employe, fullName: "Alice Employee");
        database.AddUser("inactive", UserRole.Employe, isActive: false, fullName: "Inactive Employee");
        database.AddUser("responsible", UserRole.Responsable, fullName: "Responsible Account");
        await database.AuthenticateAsync(creator);
        database.Context.ChangeTracker.Clear();

        var result = await database.EmployeeLookupService.SearchAsync(new(null, 20));

        Assert.True(result.Success);
        Assert.Contains(result.Items, item => item.UserId == creator.Id);
        Assert.Contains(result.Items, item => item.UserId == zoe.Id);
        Assert.Contains(result.Items, item => item.UserId == alice.Id);
        Assert.DoesNotContain(result.Items, item => item.DisplayName is "Inactive Employee" or "Responsible Account");
        Assert.Equal(result.Items.OrderBy(item => item.DisplayName).ThenBy(item => item.UserId), result.Items);
        Assert.All(result.Items, item => Assert.NotNull(item.Username));
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Employee_search_matches_trimmed_display_name_and_username()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("lookup-responsible", UserRole.Responsable);
        database.AddUser("atlas-login", UserRole.Employe, fullName: "Yasmine Finance");
        database.AddUser("other-login", UserRole.Employe, fullName: "Other Employee");
        await database.AuthenticateAsync(responsible);

        var byName = await database.EmployeeLookupService.SearchAsync(new("  Yasmine  ", 20));
        var byUsername = await database.EmployeeLookupService.SearchAsync(new("atlas-login", 20));
        var none = await database.EmployeeLookupService.SearchAsync(new("missing", 20));

        Assert.Equal("atlas-login", Assert.Single(byName.Items).Username);
        Assert.Equal("Yasmine Finance", Assert.Single(byUsername.Items).DisplayName);
        Assert.Empty(none.Items);
    }

    [Fact]
    public async Task Employee_lookup_honors_limit()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("limit-responsible", UserRole.Responsable);
        database.AddUser("first", UserRole.Employe, fullName: "A First");
        database.AddUser("second", UserRole.Employe, fullName: "B Second");
        await database.AuthenticateAsync(responsible);

        var result = await database.EmployeeLookupService.SearchAsync(new(null, 1));

        Assert.True(result.Success);
        Assert.Single(result.Items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Invalid_employee_lookup_limit_is_rejected(int limit)
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser($"invalid-limit-{limit}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.EmployeeLookupService.SearchAsync(new(null, limit));

        Assert.False(result.Success);
        Assert.Equal(EmployeeLookupFailureReason.InvalidLimit, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Employee_lookup_requires_authentication_and_create_permission()
    {
        using var database = new ExpenseTestDatabase();
        var employee = database.AddUser("lookup-view-only", UserRole.Employe);
        database.GrantExpenses(employee, canView: true, canCreate: false);

        var unauthenticated = await database.EmployeeLookupService.SearchAsync(new());
        await database.AuthenticateAsync(employee);
        var forbidden = await database.EmployeeLookupService.SearchAsync(new());

        Assert.Equal(EmployeeLookupFailureReason.NotAuthenticated, unauthenticated.FailureReason);
        Assert.Equal(EmployeeLookupFailureReason.Forbidden, forbidden.FailureReason);
        Assert.Empty(unauthenticated.Items);
        Assert.Empty(forbidden.Items);
    }

    [Fact]
    public async Task Employee_lookup_cancellation_is_propagated()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("cancel-lookup", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);
        using var source = new CancellationTokenSource();
        source.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.EmployeeLookupService.SearchAsync(new(), source.Token));
    }
}

public sealed class ExpenseTreasuryIntegrationTests
{
    [Fact]
    public async Task Cash_expense_is_one_derived_outflow_and_can_make_cash_negative()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("cash-finance", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);
        var opening = await database.TreasuryService.CreateOpeningBalanceAsync(
            new(ExpenseTestDatabase.BusinessDate, 50, 400));

        var create = await database.ExpenseService.CreateAsync(
            new(1, 100, PaymentMethod.Cash, null, null));
        var summary = await database.TreasuryService.GetSummaryAsync();

        Assert.True(opening.Success);
        Assert.True(create.Success);
        Assert.Equal(100, summary.ExpenseCashOutflowsMinor);
        Assert.Equal(0, summary.ExpenseBankOutflowsMinor);
        Assert.Equal(-50, summary.CashBalanceMinor);
        Assert.Equal(400, summary.BankBalanceMinor);
        Assert.Equal(350, summary.TotalTreasuryMinor);
        Assert.Single(database.Context.Expenses);
        Assert.Empty(database.Context.CustomerPayments);
        Assert.Empty(database.Context.SupplierPayments);
        Assert.Empty(database.Context.TreasuryTransfers);
        Assert.Single(database.Context.TreasuryOpeningBalances);
    }

    [Fact]
    public async Task Bank_expense_is_one_derived_outflow_and_can_make_bank_negative()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("bank-finance", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);
        await database.TreasuryService.CreateOpeningBalanceAsync(
            new(ExpenseTestDatabase.BusinessDate, 400, 40));

        var create = await database.ExpenseService.CreateAsync(
            new(1, 100, PaymentMethod.BankTransfer, null, null));
        var summary = await database.TreasuryService.GetSummaryAsync();

        Assert.True(create.Success);
        Assert.Equal(0, summary.ExpenseCashOutflowsMinor);
        Assert.Equal(100, summary.ExpenseBankOutflowsMinor);
        Assert.Equal(400, summary.CashBalanceMinor);
        Assert.Equal(-60, summary.BankBalanceMinor);
        Assert.Equal(340, summary.TotalTreasuryMinor);
        Assert.Single(database.Context.Expenses);
        Assert.Empty(database.Context.TreasuryTransfers);
    }

    [Fact]
    public async Task Expense_creation_does_not_require_treasury_initialization()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("no-opening", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var create = await database.ExpenseService.CreateAsync(
            new(1, 100, PaymentMethod.Cash, null, null));
        var summary = await database.TreasuryService.GetSummaryAsync();

        Assert.True(create.Success);
        Assert.False(summary.IsInitialized);
        Assert.Equal(-100, summary.CashBalanceMinor);
        Assert.Empty(database.Context.TreasuryOpeningBalances);
    }
}

public sealed class ExpenseDashboardIntegrationTests
{
    [Fact]
    public async Task Dashboard_includes_created_expense_in_period_with_exact_long_result()
    {
        using var database = new ExpenseTestDatabase();
        var responsible = database.AddUser("dashboard-expense", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var create = await database.ExpenseService.CreateAsync(
            new(1, 9_000_000_000, PaymentMethod.Cash, null, null));
        var inPeriod = await database.DashboardService.GetAsync(
            new(ExpenseTestDatabase.BusinessDate, ExpenseTestDatabase.BusinessDate));
        var outside = await database.DashboardService.GetAsync(
            new(ExpenseTestDatabase.BusinessDate.AddDays(1), ExpenseTestDatabase.BusinessDate.AddDays(2)));

        Assert.True(create.Success);
        Assert.Equal(ExpenseTestDatabase.BusinessDate, create.ExpenseDate);
        Assert.Equal(9_000_000_000, inPeriod.ExpensesMinor);
        Assert.Equal(-9_000_000_000, inPeriod.ResultMinor);
        Assert.Equal(0, outside.ExpensesMinor);
        Assert.Equal(0, outside.ResultMinor);
    }
}

internal sealed class ExpenseTestDatabase : IDisposable
{
    private const string Password = "Expense backend test password 42!";
    public static readonly DateTimeOffset TestUtc =
        new(2026, 9, 6, 23, 30, 0, TimeSpan.Zero);
    public static readonly DateOnly BusinessDate = new(2026, 9, 7);

    private readonly string databasePath;
    private readonly ServiceProvider serviceProvider;
    private readonly IServiceScope scope;
    private readonly IPasswordHasher passwordHasher;
    private readonly IAuthenticationService authenticationService;

    public ExpenseTestDatabase()
    {
        databasePath = Path.Combine(
            Path.GetTempPath(),
            $"FinancialManagement-Expenses-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(TestUtc));
        services.AddSingleton<IBusinessReferenceGenerator, DeterministicReferenceGenerator>();
        services.AddInfrastructure($"Data Source={databasePath};Pooling=False");
        serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
        scope = serviceProvider.CreateScope();
        Context = scope.ServiceProvider.GetRequiredService<FinancialManagementDbContext>();
        Context.Database.Migrate();
        passwordHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher>();
        authenticationService = scope.ServiceProvider.GetRequiredService<IAuthenticationService>();
        ExpenseService = scope.ServiceProvider.GetRequiredService<IExpenseService>();
        CategoryLookupService = scope.ServiceProvider.GetRequiredService<IExpenseCategoryLookupService>();
        EmployeeLookupService = scope.ServiceProvider.GetRequiredService<IEmployeeLookupService>();
        TreasuryService = scope.ServiceProvider.GetRequiredService<ITreasuryService>();
        DashboardService = scope.ServiceProvider.GetRequiredService<IFinancialDashboardService>();
    }

    public FinancialManagementDbContext Context { get; }

    public IExpenseService ExpenseService { get; }

    public IExpenseCategoryLookupService CategoryLookupService { get; }

    public IEmployeeLookupService EmployeeLookupService { get; }

    public ITreasuryService TreasuryService { get; }

    public IFinancialDashboardService DashboardService { get; }

    public User AddUser(
        string username,
        UserRole role,
        bool isActive = true,
        string? fullName = null)
    {
        var user = new User
        {
            FullName = fullName ?? $"Expense Test {username}",
            Username = username,
            PasswordHash = passwordHasher.HashPassword(Password),
            Role = role,
            IsActive = isActive,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Users.Add(user);
        Context.SaveChanges();
        return user;
    }

    public void GrantExpenses(User employee, bool canView, bool canCreate)
    {
        Context.UserPermissions.Add(new UserPermission
        {
            UserId = employee.Id,
            Module = PermissionModule.Expenses,
            CanView = canView,
            CanCreate = canCreate,
            CanModify = false
        });
        Context.SaveChanges();
    }

    public Expense AddExpense(
        User creator,
        string reference,
        DateOnly expenseDate,
        long categoryId = 1,
        long amountMinor = 100,
        PaymentMethod paymentMethod = PaymentMethod.Cash,
        long? concernedEmployeeUserId = null,
        string? observation = null)
    {
        var expense = new Expense
        {
            Reference = reference,
            ExpenseDate = expenseDate,
            ExpenseCategoryId = categoryId,
            AmountMinor = amountMinor,
            PaymentMethod = paymentMethod,
            ConcernedEmployeeUserId = concernedEmployeeUserId,
            Observation = observation,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Expenses.Add(expense);
        Context.SaveChanges();
        return expense;
    }

    public void SetCategoryActive(long categoryId, bool isActive)
    {
        var category = Context.ExpenseCategories.Single(item => item.Id == categoryId);
        category.IsActive = isActive;
        Context.SaveChanges();
    }

    public void SetUserActive(long userId, bool isActive)
    {
        var user = Context.Users.Single(item => item.Id == userId);
        user.IsActive = isActive;
        user.UpdatedAtUtc = TestUtc;
        Context.SaveChanges();
    }

    public async Task AuthenticateAsync(User user)
    {
        var result = await authenticationService.AuthenticateAsync(user.Username, Password);
        Assert.True(result.Success);
    }

    public void Dispose()
    {
        Context.Database.CloseConnection();
        scope.Dispose();
        serviceProvider.Dispose();
        SqliteConnection.ClearAllPools();

        foreach (var path in new[]
        {
            databasePath,
            $"{databasePath}-wal",
            $"{databasePath}-shm",
            $"{databasePath}-journal"
        })
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private static readonly TimeZoneInfo TestTimeZone = TimeZoneInfo.CreateCustomTimeZone(
            "FinancialManagement-Expense-Test-UTC-02",
            TimeSpan.FromHours(2),
            "FinancialManagement Expense Test UTC+02",
            "FinancialManagement Expense Test UTC+02");

        public override TimeZoneInfo LocalTimeZone => TestTimeZone;

        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class DeterministicReferenceGenerator : IBusinessReferenceGenerator
    {
        private int expenseSequence;

        public string GenerateSaleReference(DateOnly businessDate) =>
            $"VTE-{businessDate:yyyyMMdd}-000000000001";

        public string GenerateCustomerPaymentReference(DateOnly businessDate) =>
            $"ENC-{businessDate:yyyyMMdd}-000000000001";

        public string GeneratePurchaseReference(DateOnly businessDate) =>
            $"ACH-{businessDate:yyyyMMdd}-000000000001";

        public string GenerateSupplierPaymentReference(DateOnly businessDate) =>
            $"DEC-{businessDate:yyyyMMdd}-000000000001";

        public string GenerateExpenseReference(DateOnly businessDate) =>
            $"DEP-{businessDate:yyyyMMdd}-{Interlocked.Increment(ref expenseSequence):X12}";

        public string GenerateInitialCustomerReceivableReference(DateOnly historicalDate) =>
            $"CRE-{historicalDate:yyyyMMdd}-000000000001";

        public string GenerateInitialSupplierPayableReference(DateOnly historicalDate) =>
            $"DET-{historicalDate:yyyyMMdd}-000000000001";

        public string GenerateTreasuryTransferReference(DateOnly businessDate) =>
            $"TRF-{businessDate:yyyyMMdd}-000000000001";
    }
}
