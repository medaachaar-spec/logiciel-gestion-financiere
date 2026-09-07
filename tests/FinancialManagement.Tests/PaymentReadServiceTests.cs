using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using FinancialManagement.Infrastructure;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FinancialManagement.Tests;

public sealed class CustomerPaymentReadServiceTests
{
    [Fact]
    public async Task Unauthenticated_customer_payment_reads_are_rejected()
    {
        using var database = new PaymentReadTestDatabase();

        var list = await database.CustomerPaymentService.GetPageAsync(new());
        var detail = await database.CustomerPaymentService.GetByIdAsync(1);

        Assert.Equal(CustomerPaymentListFailureReason.NotAuthenticated, list.FailureReason);
        Assert.Equal(CustomerPaymentDetailFailureReason.NotAuthenticated, detail.FailureReason);
        Assert.Empty(list.Items);
        Assert.Null(detail.Item);
    }

    [Fact]
    public async Task Customer_payment_view_permission_is_required()
    {
        using var database = new PaymentReadTestDatabase();
        var employee = database.AddUser("customer-denied", UserRole.Employe);
        database.GrantView(employee, PermissionModule.CustomerPayments, canView: false);
        var client = database.AddClient("Secret Client");
        var payment = database.AddCustomerPayment(employee, "ENC-SECRET", Date(1), 100, client.Id);
        await database.AuthenticateAsync(employee);

        var list = await database.CustomerPaymentService.GetPageAsync(new());
        var detail = await database.CustomerPaymentService.GetByIdAsync(payment.Id);

        Assert.Equal(CustomerPaymentListFailureReason.Forbidden, list.FailureReason);
        Assert.Equal(CustomerPaymentDetailFailureReason.Forbidden, detail.FailureReason);
        Assert.Empty(list.Items);
        Assert.Null(detail.Item);
    }

    [Fact]
    public async Task Empty_customer_payment_list_uses_default_pagination()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-empty", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerPaymentService.GetPageAsync(new());

        Assert.True(result.Success);
        Assert.Equal(CustomerPaymentListFailureReason.None, result.FailureReason);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task Customer_payment_pagination_uses_date_then_id_descending()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-pages", UserRole.Responsable);
        var client = database.AddClient("Page Client");
        var first = database.AddCustomerPayment(responsible, "ENC-1", Date(2), 101, client.Id);
        var second = database.AddCustomerPayment(responsible, "ENC-2", Date(2), 102, client.Id);
        var third = database.AddCustomerPayment(responsible, "ENC-3", Date(3), 103, client.Id);
        var fourth = database.AddCustomerPayment(responsible, "ENC-4", Date(1), 104, client.Id);
        var fifth = database.AddCustomerPayment(responsible, "ENC-5", Date(1), 105, client.Id);
        await database.AuthenticateAsync(responsible);

        var page1 = await database.CustomerPaymentService.GetPageAsync(new(PageSize: 2));
        var page2 = await database.CustomerPaymentService.GetPageAsync(new(PageNumber: 2, PageSize: 2));
        var page3 = await database.CustomerPaymentService.GetPageAsync(new(PageNumber: 3, PageSize: 2));

        Assert.Equal([third.Id, second.Id], page1.Items.Select(item => item.Id));
        Assert.Equal([first.Id, fifth.Id], page2.Items.Select(item => item.Id));
        Assert.Equal([fourth.Id], page3.Items.Select(item => item.Id));
        Assert.All([page1, page2, page3], page => Assert.Equal(5, page.TotalCount));
        Assert.All([page1, page2, page3], page => Assert.Equal(3, page.TotalPages));
    }

    [Theory]
    [InlineData(0, 50, CustomerPaymentListFailureReason.InvalidPageNumber)]
    [InlineData(1, 0, CustomerPaymentListFailureReason.InvalidPageSize)]
    [InlineData(1, 201, CustomerPaymentListFailureReason.InvalidPageSize)]
    [InlineData(int.MaxValue, 200, CustomerPaymentListFailureReason.InvalidPageNumber)]
    public async Task Invalid_customer_payment_pagination_is_rejected(
        int pageNumber,
        int pageSize,
        CustomerPaymentListFailureReason expected)
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser($"customer-page-{pageNumber}-{pageSize}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerPaymentService.GetPageAsync(
            new(pageNumber, pageSize));

        Assert.False(result.Success);
        Assert.Equal(expected, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Customer_payment_date_filters_are_inclusive()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-dates", UserRole.Responsable);
        var client = database.AddClient("Date Client");
        database.AddCustomerPayment(responsible, "ENC-D1", Date(1), 100, client.Id);
        database.AddCustomerPayment(responsible, "ENC-D2", Date(2), 200, client.Id);
        database.AddCustomerPayment(responsible, "ENC-D3", Date(3), 300, client.Id);
        await database.AuthenticateAsync(responsible);

        var from = await database.CustomerPaymentService.GetPageAsync(new(FromDate: Date(2)));
        var to = await database.CustomerPaymentService.GetPageAsync(new(ToDate: Date(2)));
        var exact = await database.CustomerPaymentService.GetPageAsync(
            new(FromDate: Date(2), ToDate: Date(2)));

        Assert.Equal(["ENC-D3", "ENC-D2"], from.Items.Select(item => item.Reference));
        Assert.Equal(["ENC-D2", "ENC-D1"], to.Items.Select(item => item.Reference));
        Assert.Equal(["ENC-D2"], exact.Items.Select(item => item.Reference));
    }

    [Fact]
    public async Task Invalid_customer_payment_date_range_is_rejected()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-date-invalid", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerPaymentService.GetPageAsync(
            new(FromDate: Date(2), ToDate: Date(1)));

        Assert.Equal(CustomerPaymentListFailureReason.InvalidDateRange, result.FailureReason);
    }

    [Fact]
    public async Task Customer_payment_search_uses_trimmed_reference_or_current_client_name_only()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-search", UserRole.Responsable);
        var atlas = database.AddClient("Atlas Client");
        var cedar = database.AddClient("Cedar Client");
        database.AddCustomerPayment(responsible, "ENC-ALPHA", Date(1), 100, atlas.Id);
        database.AddCustomerPayment(
            responsible,
            "ENC-BETA",
            Date(2),
            200,
            cedar.Id,
            observation: "HiddenNeedle");
        await database.AuthenticateAsync(responsible);

        var byReference = await database.CustomerPaymentService.GetPageAsync(
            new(SearchText: "  ALPHA  "));
        var byClient = await database.CustomerPaymentService.GetPageAsync(
            new(SearchText: "Cedar"));
        var whitespace = await database.CustomerPaymentService.GetPageAsync(
            new(SearchText: "   "));
        var observation = await database.CustomerPaymentService.GetPageAsync(
            new(SearchText: "HiddenNeedle"));

        Assert.Equal(["ENC-ALPHA"], byReference.Items.Select(item => item.Reference));
        Assert.Equal(["ENC-BETA"], byClient.Items.Select(item => item.Reference));
        Assert.Equal(2, whitespace.TotalCount);
        Assert.Empty(observation.Items);

        cedar.Name = "Renamed Client";
        database.Context.SaveChanges();
        var renamed = await database.CustomerPaymentService.GetPageAsync(
            new(SearchText: "Renamed"));
        Assert.Equal(["ENC-BETA"], renamed.Items.Select(item => item.Reference));
    }

    [Fact]
    public async Task Customer_payment_method_filter_is_exact()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-method", UserRole.Responsable);
        var client = database.AddClient("Method Client");
        database.AddCustomerPayment(responsible, "ENC-CASH", Date(1), 100, client.Id);
        database.AddCustomerPayment(
            responsible,
            "ENC-BANK",
            Date(2),
            200,
            client.Id,
            PaymentMethod.BankTransfer);
        await database.AuthenticateAsync(responsible);

        var cash = await database.CustomerPaymentService.GetPageAsync(
            new(PaymentMethod: PaymentMethod.Cash));
        var bank = await database.CustomerPaymentService.GetPageAsync(
            new(PaymentMethod: PaymentMethod.BankTransfer));

        Assert.Equal(["ENC-CASH"], cash.Items.Select(item => item.Reference));
        Assert.Equal(["ENC-BANK"], bank.Items.Select(item => item.Reference));
    }

    [Fact]
    public async Task Undefined_customer_payment_method_filter_is_rejected()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-method-invalid", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerPaymentService.GetPageAsync(
            new(PaymentMethod: (PaymentMethod)999));

        Assert.Equal(CustomerPaymentListFailureReason.InvalidPaymentMethod, result.FailureReason);
    }

    [Fact]
    public async Task Anonymous_customer_payment_keeps_nullable_party_fields()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-anonymous", UserRole.Responsable);
        var sale = database.AddSale(responsible, "VTE-ANON", 125_075, clientId: null);
        var payment = database.AddCustomerPayment(
            responsible,
            "ENC-ANON",
            Date(1),
            125_075,
            clientId: null);
        database.AddCustomerAllocation(payment, 125_075, saleId: sale.Id);
        await database.AuthenticateAsync(responsible);

        var list = await database.CustomerPaymentService.GetPageAsync(new());
        var detail = await database.CustomerPaymentService.GetByIdAsync(payment.Id);

        var listItem = Assert.Single(list.Items);
        Assert.Null(listItem.ClientId);
        Assert.Null(listItem.ClientName);
        Assert.Equal(0, listItem.UnallocatedAmountMinor);
        Assert.Null(Assert.IsType<CustomerPaymentDetailItem>(detail.Item).ClientId);
    }

    [Fact]
    public async Task Customer_list_projects_zero_partial_full_multiple_and_positive_unallocated_amounts()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-aggregates", UserRole.Responsable);
        var client = database.AddClient("Aggregate Client");
        var sale1 = database.AddSale(responsible, "VTE-A1", 10_000_000_000, client.Id);
        var sale2 = database.AddSale(responsible, "VTE-A2", 10_000_000_000, client.Id);
        var zero = database.AddCustomerPayment(responsible, "ENC-ZERO", Date(1), 9_000_000_000, client.Id);
        var partial = database.AddCustomerPayment(responsible, "ENC-PART", Date(2), 1_000, client.Id);
        var full = database.AddCustomerPayment(responsible, "ENC-FULL", Date(3), 1_000, client.Id);
        var multiple = database.AddCustomerPayment(responsible, "ENC-MULTI", Date(4), 1_000, client.Id);
        database.AddCustomerAllocation(partial, 400, saleId: sale1.Id);
        database.AddCustomerAllocation(full, 1_000, saleId: sale1.Id);
        database.AddCustomerAllocation(multiple, 300, saleId: sale1.Id);
        database.AddCustomerAllocation(multiple, 200, saleId: sale2.Id);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerPaymentService.GetPageAsync(new());

        AssertAmounts(result, zero.Id, 9_000_000_000, 0, 9_000_000_000);
        AssertAmounts(result, partial.Id, 1_000, 400, 600);
        AssertAmounts(result, full.Id, 1_000, 1_000, 0);
        AssertAmounts(result, multiple.Id, 1_000, 500, 500);
    }

    [Theory]
    [InlineData(0, CustomerPaymentDetailFailureReason.InvalidPaymentId)]
    [InlineData(-1, CustomerPaymentDetailFailureReason.InvalidPaymentId)]
    [InlineData(999_999, CustomerPaymentDetailFailureReason.NotFound)]
    public async Task Invalid_or_missing_customer_payment_detail_is_controlled(
        long paymentId,
        CustomerPaymentDetailFailureReason expected)
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser($"customer-detail-{paymentId}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerPaymentService.GetByIdAsync(paymentId);

        Assert.Equal(expected, result.FailureReason);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task Customer_detail_projects_header_and_both_target_types()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-detail", UserRole.Responsable);
        var client = database.AddClient("Detail Client");
        var sale = database.AddSale(responsible, "VTE-DETAIL", 9_000_000_000, client.Id);
        var receivable = database.AddReceivable(
            responsible,
            client,
            "CRE-DETAIL",
            8_000_000_000);
        var payment = database.AddCustomerPayment(
            responsible,
            "ENC-DETAIL",
            Date(4),
            9_500_000_000,
            client.Id,
            PaymentMethod.BankTransfer,
            "Exact observation");
        var saleAllocation = database.AddCustomerAllocation(payment, 4_000_000_000, saleId: sale.Id);
        var receivableAllocation = database.AddCustomerAllocation(
            payment,
            3_000_000_000,
            receivableId: receivable.Id);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerPaymentService.GetByIdAsync(payment.Id);

        Assert.True(result.Success);
        var item = Assert.IsType<CustomerPaymentDetailItem>(result.Item);
        Assert.Equal(payment.Id, item.Id);
        Assert.Equal("ENC-DETAIL", item.Reference);
        Assert.Equal(Date(4), item.PaymentDate);
        Assert.Equal(client.Id, item.ClientId);
        Assert.Equal("Detail Client", item.ClientName);
        Assert.Equal(9_500_000_000, item.AmountMinor);
        Assert.Equal(PaymentMethod.BankTransfer, item.PaymentMethod);
        Assert.Equal(7_000_000_000, item.AllocatedAmountMinor);
        Assert.Equal(2_500_000_000, item.UnallocatedAmountMinor);
        Assert.Equal("Exact observation", item.Observation);

        var saleItem = Assert.Single(item.Allocations, allocation => allocation.Id == saleAllocation.Id);
        Assert.Equal(CustomerPaymentAllocationTargetType.Sale, saleItem.TargetType);
        Assert.Equal(sale.Id, saleItem.TargetId);
        Assert.Equal("VTE-DETAIL", saleItem.TargetReference);
        Assert.Equal(9_000_000_000, saleItem.OriginalObligationAmountMinor);
        Assert.Equal(4_000_000_000, saleItem.AllocatedAmountMinor);

        var receivableItem = Assert.Single(
            item.Allocations,
            allocation => allocation.Id == receivableAllocation.Id);
        Assert.Equal(
            CustomerPaymentAllocationTargetType.InitialCustomerReceivable,
            receivableItem.TargetType);
        Assert.Equal(receivable.Id, receivableItem.TargetId);
        Assert.Equal("CRE-DETAIL", receivableItem.TargetReference);
        Assert.Equal(8_000_000_000, receivableItem.OriginalObligationAmountMinor);
        Assert.Equal(3_000_000_000, receivableItem.AllocatedAmountMinor);
    }

    [Fact]
    public async Task Customer_reads_are_no_tracking_and_do_not_mutate_data()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-no-mutation", UserRole.Responsable);
        var client = database.AddClient("Unchanged Client");
        var payment = database.AddCustomerPayment(
            responsible,
            "ENC-UNCHANGED",
            Date(1),
            500,
            client.Id,
            observation: "Original");
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var list = await database.CustomerPaymentService.GetPageAsync(new());
        var detail = await database.CustomerPaymentService.GetByIdAsync(payment.Id);

        Assert.True(list.Success);
        Assert.True(detail.Success);
        Assert.Empty(database.Context.ChangeTracker.Entries());
        var persisted = await database.Context.CustomerPayments.AsNoTracking().SingleAsync();
        Assert.Equal("Original", persisted.Observation);
        Assert.Equal(500, persisted.AmountMinor);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Customer_reads_honor_cancellation_before_query()
    {
        using var database = new PaymentReadTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.CustomerPaymentService.GetPageAsync(new(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.CustomerPaymentService.GetByIdAsync(1, cancellation.Token));
    }

    [Fact]
    public async Task Customer_overallocated_payment_is_reported_as_data_integrity_violation()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-corruption", UserRole.Responsable);
        var client = database.AddClient("Corrupt Client");
        var sale = database.AddSale(responsible, "VTE-CORRUPT", 1_000, client.Id);
        var payment = database.AddCustomerPayment(responsible, "ENC-CORRUPT", Date(1), 100, client.Id);
        database.AddCustomerAllocation(payment, 101, saleId: sale.Id);
        await database.AuthenticateAsync(responsible);

        var list = await database.CustomerPaymentService.GetPageAsync(new());
        var detail = await database.CustomerPaymentService.GetByIdAsync(payment.Id);

        Assert.Equal(CustomerPaymentListFailureReason.DataIntegrityViolation, list.FailureReason);
        Assert.Empty(list.Items);
        Assert.Equal(CustomerPaymentDetailFailureReason.DataIntegrityViolation, detail.FailureReason);
        Assert.Null(detail.Item);
        Assert.Equal(101, await database.Context.CustomerPaymentAllocations.SumAsync(item => item.AmountMinor));
    }

    private static DateOnly Date(int day) => new(2026, 9, day);

    private static void AssertAmounts(
        CustomerPaymentListResult result,
        long id,
        long amountMinor,
        long allocatedAmountMinor,
        long unallocatedAmountMinor)
    {
        var item = Assert.Single(result.Items, candidate => candidate.Id == id);
        Assert.Equal(amountMinor, item.AmountMinor);
        Assert.Equal(allocatedAmountMinor, item.AllocatedAmountMinor);
        Assert.Equal(unallocatedAmountMinor, item.UnallocatedAmountMinor);
    }
}

public sealed class SupplierPaymentReadServiceTests
{
    [Fact]
    public async Task Unauthenticated_supplier_payment_reads_are_rejected()
    {
        using var database = new PaymentReadTestDatabase();

        var list = await database.SupplierPaymentService.GetPageAsync(new());
        var detail = await database.SupplierPaymentService.GetByIdAsync(1);

        Assert.Equal(SupplierPaymentListFailureReason.NotAuthenticated, list.FailureReason);
        Assert.Equal(SupplierPaymentDetailFailureReason.NotAuthenticated, detail.FailureReason);
    }

    [Fact]
    public async Task Supplier_payment_view_permission_is_required()
    {
        using var database = new PaymentReadTestDatabase();
        var employee = database.AddUser("supplier-denied", UserRole.Employe);
        database.GrantView(employee, PermissionModule.SupplierPayments, canView: false);
        var supplier = database.AddSupplier("Secret Supplier");
        var payment = database.AddSupplierPayment(employee, "DEC-SECRET", Date(1), 100, supplier.Id);
        await database.AuthenticateAsync(employee);

        var list = await database.SupplierPaymentService.GetPageAsync(new());
        var detail = await database.SupplierPaymentService.GetByIdAsync(payment.Id);

        Assert.Equal(SupplierPaymentListFailureReason.Forbidden, list.FailureReason);
        Assert.Equal(SupplierPaymentDetailFailureReason.Forbidden, detail.FailureReason);
        Assert.Empty(list.Items);
        Assert.Null(detail.Item);
    }

    [Fact]
    public async Task Empty_supplier_payment_list_uses_default_pagination()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("supplier-empty", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierPaymentService.GetPageAsync(new());

        Assert.True(result.Success);
        Assert.Empty(result.Items);
        Assert.Equal(1, result.PageNumber);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(0, result.TotalCount);
        Assert.Equal(0, result.TotalPages);
    }

    [Fact]
    public async Task Supplier_payment_pagination_uses_date_then_id_descending()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("supplier-pages", UserRole.Responsable);
        var supplier = database.AddSupplier("Page Supplier");
        var first = database.AddSupplierPayment(responsible, "DEC-1", Date(2), 101, supplier.Id);
        var second = database.AddSupplierPayment(responsible, "DEC-2", Date(2), 102, supplier.Id);
        var third = database.AddSupplierPayment(responsible, "DEC-3", Date(3), 103, supplier.Id);
        var fourth = database.AddSupplierPayment(responsible, "DEC-4", Date(1), 104, supplier.Id);
        await database.AuthenticateAsync(responsible);

        var page1 = await database.SupplierPaymentService.GetPageAsync(new(PageSize: 2));
        var page2 = await database.SupplierPaymentService.GetPageAsync(new(PageNumber: 2, PageSize: 2));

        Assert.Equal([third.Id, second.Id], page1.Items.Select(item => item.Id));
        Assert.Equal([first.Id, fourth.Id], page2.Items.Select(item => item.Id));
        Assert.All([page1, page2], page => Assert.Equal(4, page.TotalCount));
        Assert.All([page1, page2], page => Assert.Equal(2, page.TotalPages));
    }

    [Theory]
    [InlineData(0, 50, SupplierPaymentListFailureReason.InvalidPageNumber)]
    [InlineData(1, 0, SupplierPaymentListFailureReason.InvalidPageSize)]
    [InlineData(1, 201, SupplierPaymentListFailureReason.InvalidPageSize)]
    [InlineData(int.MaxValue, 200, SupplierPaymentListFailureReason.InvalidPageNumber)]
    public async Task Invalid_supplier_payment_pagination_is_rejected(
        int pageNumber,
        int pageSize,
        SupplierPaymentListFailureReason expected)
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser($"supplier-page-{pageNumber}-{pageSize}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierPaymentService.GetPageAsync(new(pageNumber, pageSize));

        Assert.Equal(expected, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task Supplier_payment_date_filters_are_inclusive_and_invalid_range_is_rejected()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("supplier-dates", UserRole.Responsable);
        var supplier = database.AddSupplier("Date Supplier");
        database.AddSupplierPayment(responsible, "DEC-D1", Date(1), 100, supplier.Id);
        database.AddSupplierPayment(responsible, "DEC-D2", Date(2), 200, supplier.Id);
        database.AddSupplierPayment(responsible, "DEC-D3", Date(3), 300, supplier.Id);
        await database.AuthenticateAsync(responsible);

        var from = await database.SupplierPaymentService.GetPageAsync(new(FromDate: Date(2)));
        var to = await database.SupplierPaymentService.GetPageAsync(new(ToDate: Date(2)));
        var exact = await database.SupplierPaymentService.GetPageAsync(
            new(FromDate: Date(2), ToDate: Date(2)));
        var invalid = await database.SupplierPaymentService.GetPageAsync(
            new(FromDate: Date(2), ToDate: Date(1)));

        Assert.Equal(["DEC-D3", "DEC-D2"], from.Items.Select(item => item.Reference));
        Assert.Equal(["DEC-D2", "DEC-D1"], to.Items.Select(item => item.Reference));
        Assert.Equal(["DEC-D2"], exact.Items.Select(item => item.Reference));
        Assert.Equal(SupplierPaymentListFailureReason.InvalidDateRange, invalid.FailureReason);
    }

    [Fact]
    public async Task Supplier_payment_search_uses_trimmed_reference_or_current_supplier_name_only()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("supplier-search", UserRole.Responsable);
        var atlas = database.AddSupplier("Atlas Supplier");
        var cedar = database.AddSupplier("Cedar Supplier");
        database.AddSupplierPayment(responsible, "DEC-ALPHA", Date(1), 100, atlas.Id);
        database.AddSupplierPayment(
            responsible,
            "DEC-BETA",
            Date(2),
            200,
            cedar.Id,
            observation: "HiddenNeedle");
        await database.AuthenticateAsync(responsible);

        var byReference = await database.SupplierPaymentService.GetPageAsync(
            new(SearchText: "  ALPHA  "));
        var bySupplier = await database.SupplierPaymentService.GetPageAsync(
            new(SearchText: "Cedar"));
        var whitespace = await database.SupplierPaymentService.GetPageAsync(
            new(SearchText: "   "));
        var observation = await database.SupplierPaymentService.GetPageAsync(
            new(SearchText: "HiddenNeedle"));

        Assert.Equal(["DEC-ALPHA"], byReference.Items.Select(item => item.Reference));
        Assert.Equal(["DEC-BETA"], bySupplier.Items.Select(item => item.Reference));
        Assert.Equal(2, whitespace.TotalCount);
        Assert.Empty(observation.Items);

        cedar.Name = "Renamed Supplier";
        database.Context.SaveChanges();
        var renamed = await database.SupplierPaymentService.GetPageAsync(
            new(SearchText: "Renamed"));
        Assert.Equal(["DEC-BETA"], renamed.Items.Select(item => item.Reference));
    }

    [Fact]
    public async Task Supplier_payment_method_filter_is_exact_and_rejects_undefined_values()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("supplier-method", UserRole.Responsable);
        var supplier = database.AddSupplier("Method Supplier");
        database.AddSupplierPayment(responsible, "DEC-CASH", Date(1), 100, supplier.Id);
        database.AddSupplierPayment(
            responsible,
            "DEC-BANK",
            Date(2),
            200,
            supplier.Id,
            PaymentMethod.BankTransfer);
        await database.AuthenticateAsync(responsible);

        var cash = await database.SupplierPaymentService.GetPageAsync(
            new(PaymentMethod: PaymentMethod.Cash));
        var bank = await database.SupplierPaymentService.GetPageAsync(
            new(PaymentMethod: PaymentMethod.BankTransfer));
        var invalid = await database.SupplierPaymentService.GetPageAsync(
            new(PaymentMethod: (PaymentMethod)999));

        Assert.Equal(["DEC-CASH"], cash.Items.Select(item => item.Reference));
        Assert.Equal(["DEC-BANK"], bank.Items.Select(item => item.Reference));
        Assert.Equal(SupplierPaymentListFailureReason.InvalidPaymentMethod, invalid.FailureReason);
    }

    [Fact]
    public async Task Supplier_list_projects_zero_partial_full_multiple_and_positive_unallocated_amounts()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("supplier-aggregates", UserRole.Responsable);
        var supplier = database.AddSupplier("Aggregate Supplier");
        var purchase1 = database.AddPurchase(responsible, supplier, "ACH-A1", 10_000_000_000);
        var purchase2 = database.AddPurchase(responsible, supplier, "ACH-A2", 10_000_000_000);
        var zero = database.AddSupplierPayment(responsible, "DEC-ZERO", Date(1), 9_000_000_000, supplier.Id);
        var partial = database.AddSupplierPayment(responsible, "DEC-PART", Date(2), 1_000, supplier.Id);
        var full = database.AddSupplierPayment(responsible, "DEC-FULL", Date(3), 1_000, supplier.Id);
        var multiple = database.AddSupplierPayment(responsible, "DEC-MULTI", Date(4), 1_000, supplier.Id);
        database.AddSupplierAllocation(partial, 400, purchaseId: purchase1.Id);
        database.AddSupplierAllocation(full, 1_000, purchaseId: purchase1.Id);
        database.AddSupplierAllocation(multiple, 300, purchaseId: purchase1.Id);
        database.AddSupplierAllocation(multiple, 200, purchaseId: purchase2.Id);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierPaymentService.GetPageAsync(new());

        AssertAmounts(result, zero.Id, 9_000_000_000, 0, 9_000_000_000);
        AssertAmounts(result, partial.Id, 1_000, 400, 600);
        AssertAmounts(result, full.Id, 1_000, 1_000, 0);
        AssertAmounts(result, multiple.Id, 1_000, 500, 500);
    }

    [Theory]
    [InlineData(0, SupplierPaymentDetailFailureReason.InvalidPaymentId)]
    [InlineData(-1, SupplierPaymentDetailFailureReason.InvalidPaymentId)]
    [InlineData(999_999, SupplierPaymentDetailFailureReason.NotFound)]
    public async Task Invalid_or_missing_supplier_payment_detail_is_controlled(
        long paymentId,
        SupplierPaymentDetailFailureReason expected)
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser($"supplier-detail-{paymentId}", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierPaymentService.GetByIdAsync(paymentId);

        Assert.Equal(expected, result.FailureReason);
        Assert.Null(result.Item);
    }

    [Fact]
    public async Task Supplier_detail_projects_header_and_both_target_types()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("supplier-detail", UserRole.Responsable);
        var supplier = database.AddSupplier("Detail Supplier");
        var purchase = database.AddPurchase(
            responsible,
            supplier,
            "ACH-DETAIL",
            9_000_000_000);
        var payable = database.AddPayable(
            responsible,
            supplier,
            "DET-DETAIL",
            8_000_000_000);
        var payment = database.AddSupplierPayment(
            responsible,
            "DEC-DETAIL",
            Date(4),
            9_500_000_000,
            supplier.Id,
            PaymentMethod.BankTransfer,
            "Exact observation");
        var purchaseAllocation = database.AddSupplierAllocation(
            payment,
            4_000_000_000,
            purchaseId: purchase.Id);
        var payableAllocation = database.AddSupplierAllocation(
            payment,
            3_000_000_000,
            payableId: payable.Id);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierPaymentService.GetByIdAsync(payment.Id);

        Assert.True(result.Success);
        var item = Assert.IsType<SupplierPaymentDetailItem>(result.Item);
        Assert.Equal(payment.Id, item.Id);
        Assert.Equal("DEC-DETAIL", item.Reference);
        Assert.Equal(Date(4), item.PaymentDate);
        Assert.Equal(supplier.Id, item.SupplierId);
        Assert.Equal("Detail Supplier", item.SupplierName);
        Assert.Equal(9_500_000_000, item.AmountMinor);
        Assert.Equal(PaymentMethod.BankTransfer, item.PaymentMethod);
        Assert.Equal(7_000_000_000, item.AllocatedAmountMinor);
        Assert.Equal(2_500_000_000, item.UnallocatedAmountMinor);
        Assert.Equal("Exact observation", item.Observation);

        var purchaseItem = Assert.Single(
            item.Allocations,
            allocation => allocation.Id == purchaseAllocation.Id);
        Assert.Equal(SupplierPaymentAllocationTargetType.Purchase, purchaseItem.TargetType);
        Assert.Equal(purchase.Id, purchaseItem.TargetId);
        Assert.Equal("ACH-DETAIL", purchaseItem.TargetReference);
        Assert.Equal(9_000_000_000, purchaseItem.OriginalObligationAmountMinor);
        Assert.Equal(4_000_000_000, purchaseItem.AllocatedAmountMinor);

        var payableItem = Assert.Single(
            item.Allocations,
            allocation => allocation.Id == payableAllocation.Id);
        Assert.Equal(
            SupplierPaymentAllocationTargetType.InitialSupplierPayable,
            payableItem.TargetType);
        Assert.Equal(payable.Id, payableItem.TargetId);
        Assert.Equal("DET-DETAIL", payableItem.TargetReference);
        Assert.Equal(8_000_000_000, payableItem.OriginalObligationAmountMinor);
        Assert.Equal(3_000_000_000, payableItem.AllocatedAmountMinor);
    }

    [Fact]
    public async Task Supplier_reads_are_no_tracking_and_honor_cancellation()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("supplier-no-mutation", UserRole.Responsable);
        var supplier = database.AddSupplier("Unchanged Supplier");
        var payment = database.AddSupplierPayment(
            responsible,
            "DEC-UNCHANGED",
            Date(1),
            500,
            supplier.Id,
            observation: "Original");
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var list = await database.SupplierPaymentService.GetPageAsync(new());
        var detail = await database.SupplierPaymentService.GetByIdAsync(payment.Id);

        Assert.True(list.Success);
        Assert.True(detail.Success);
        Assert.Empty(database.Context.ChangeTracker.Entries());
        var persisted = await database.Context.SupplierPayments.AsNoTracking().SingleAsync();
        Assert.Equal("Original", persisted.Observation);
        Assert.Equal(500, persisted.AmountMinor);
        Assert.Empty(database.Context.ChangeTracker.Entries());

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SupplierPaymentService.GetPageAsync(new(), cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SupplierPaymentService.GetByIdAsync(payment.Id, cancellation.Token));
    }

    [Fact]
    public async Task Supplier_overallocated_payment_is_reported_as_data_integrity_violation()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("supplier-corruption", UserRole.Responsable);
        var supplier = database.AddSupplier("Corrupt Supplier");
        var purchase = database.AddPurchase(responsible, supplier, "ACH-CORRUPT", 1_000);
        var payment = database.AddSupplierPayment(responsible, "DEC-CORRUPT", Date(1), 100, supplier.Id);
        database.AddSupplierAllocation(payment, 101, purchaseId: purchase.Id);
        await database.AuthenticateAsync(responsible);

        var list = await database.SupplierPaymentService.GetPageAsync(new());
        var detail = await database.SupplierPaymentService.GetByIdAsync(payment.Id);

        Assert.Equal(SupplierPaymentListFailureReason.DataIntegrityViolation, list.FailureReason);
        Assert.Empty(list.Items);
        Assert.Equal(SupplierPaymentDetailFailureReason.DataIntegrityViolation, detail.FailureReason);
        Assert.Null(detail.Item);
        Assert.Equal(101, await database.Context.SupplierPaymentAllocations.SumAsync(item => item.AmountMinor));
    }

    private static DateOnly Date(int day) => new(2026, 9, day);

    private static void AssertAmounts(
        SupplierPaymentListResult result,
        long id,
        long amountMinor,
        long allocatedAmountMinor,
        long unallocatedAmountMinor)
    {
        var item = Assert.Single(result.Items, candidate => candidate.Id == id);
        Assert.Equal(amountMinor, item.AmountMinor);
        Assert.Equal(allocatedAmountMinor, item.AllocatedAmountMinor);
        Assert.Equal(unallocatedAmountMinor, item.UnallocatedAmountMinor);
    }
}

internal sealed class PaymentReadTestDatabase : IDisposable
{
    private const string Password = "Payment read test password 42!";
    private static readonly DateTimeOffset TestUtc =
        new(2026, 9, 6, 10, 0, 0, TimeSpan.Zero);

    private readonly string databasePath;
    private readonly ServiceProvider serviceProvider;
    private readonly IServiceScope scope;
    private readonly IPasswordHasher passwordHasher;
    private readonly IAuthenticationService authenticationService;

    public PaymentReadTestDatabase()
    {
        databasePath = Path.Combine(
            Path.GetTempPath(),
            $"FinancialManagement-PaymentRead-{Guid.NewGuid():N}.db");
        var services = new ServiceCollection();
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
        CustomerPaymentService = scope.ServiceProvider.GetRequiredService<ICustomerPaymentService>();
        SupplierPaymentService = scope.ServiceProvider.GetRequiredService<ISupplierPaymentService>();
    }

    public FinancialManagementDbContext Context { get; }

    public ICustomerPaymentService CustomerPaymentService { get; }

    public ISupplierPaymentService SupplierPaymentService { get; }

    public User AddUser(string username, UserRole role)
    {
        var user = new User
        {
            FullName = $"Payment Read Test {username}",
            Username = username,
            PasswordHash = passwordHasher.HashPassword(Password),
            Role = role,
            IsActive = true,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Users.Add(user);
        Context.SaveChanges();
        return user;
    }

    public Client AddClient(string name)
    {
        var client = new Client
        {
            Name = name,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Clients.Add(client);
        Context.SaveChanges();
        return client;
    }

    public Supplier AddSupplier(string name)
    {
        var supplier = new Supplier
        {
            Name = name,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Suppliers.Add(supplier);
        Context.SaveChanges();
        return supplier;
    }

    public Sale AddSale(
        User creator,
        string reference,
        long amountMinor,
        long? clientId)
    {
        var sale = new Sale
        {
            Reference = reference,
            SaleDate = new DateOnly(2026, 9, 1),
            TotalAmountMinor = amountMinor,
            ClientId = clientId,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Sales.Add(sale);
        Context.SaveChanges();
        return sale;
    }

    public InitialCustomerReceivable AddReceivable(
        User creator,
        Client client,
        string reference,
        long amountMinor)
    {
        var receivable = new InitialCustomerReceivable
        {
            Reference = reference,
            ClientId = client.Id,
            ReceivableDate = new DateOnly(2026, 8, 1),
            InitialAmountMinor = amountMinor,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc
        };
        Context.InitialCustomerReceivables.Add(receivable);
        Context.SaveChanges();
        return receivable;
    }

    public Purchase AddPurchase(
        User creator,
        Supplier supplier,
        string reference,
        long amountMinor)
    {
        var purchase = new Purchase
        {
            Reference = reference,
            PurchaseDate = new DateOnly(2026, 9, 1),
            TotalAmountMinor = amountMinor,
            SupplierId = supplier.Id,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc,
            UpdatedAtUtc = TestUtc
        };
        Context.Purchases.Add(purchase);
        Context.SaveChanges();
        return purchase;
    }

    public InitialSupplierPayable AddPayable(
        User creator,
        Supplier supplier,
        string reference,
        long amountMinor)
    {
        var payable = new InitialSupplierPayable
        {
            Reference = reference,
            SupplierId = supplier.Id,
            PayableDate = new DateOnly(2026, 8, 1),
            InitialAmountMinor = amountMinor,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc
        };
        Context.InitialSupplierPayables.Add(payable);
        Context.SaveChanges();
        return payable;
    }

    public CustomerPayment AddCustomerPayment(
        User creator,
        string reference,
        DateOnly paymentDate,
        long amountMinor,
        long? clientId,
        PaymentMethod paymentMethod = PaymentMethod.Cash,
        string? observation = null)
    {
        var payment = new CustomerPayment
        {
            Reference = reference,
            ClientId = clientId,
            PaymentDate = paymentDate,
            AmountMinor = amountMinor,
            PaymentMethod = paymentMethod,
            Observation = observation,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc
        };
        Context.CustomerPayments.Add(payment);
        Context.SaveChanges();
        return payment;
    }

    public SupplierPayment AddSupplierPayment(
        User creator,
        string reference,
        DateOnly paymentDate,
        long amountMinor,
        long supplierId,
        PaymentMethod paymentMethod = PaymentMethod.Cash,
        string? observation = null)
    {
        var payment = new SupplierPayment
        {
            Reference = reference,
            SupplierId = supplierId,
            PaymentDate = paymentDate,
            AmountMinor = amountMinor,
            PaymentMethod = paymentMethod,
            Observation = observation,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc
        };
        Context.SupplierPayments.Add(payment);
        Context.SaveChanges();
        return payment;
    }

    public CustomerPaymentAllocation AddCustomerAllocation(
        CustomerPayment payment,
        long amountMinor,
        long? saleId = null,
        long? receivableId = null)
    {
        var allocation = new CustomerPaymentAllocation
        {
            CustomerPaymentId = payment.Id,
            SaleId = saleId,
            InitialCustomerReceivableId = receivableId,
            AmountMinor = amountMinor,
            CreatedAtUtc = TestUtc
        };
        Context.CustomerPaymentAllocations.Add(allocation);
        Context.SaveChanges();
        return allocation;
    }

    public SupplierPaymentAllocation AddSupplierAllocation(
        SupplierPayment payment,
        long amountMinor,
        long? purchaseId = null,
        long? payableId = null)
    {
        var allocation = new SupplierPaymentAllocation
        {
            SupplierPaymentId = payment.Id,
            PurchaseId = purchaseId,
            InitialSupplierPayableId = payableId,
            AmountMinor = amountMinor,
            CreatedAtUtc = TestUtc
        };
        Context.SupplierPaymentAllocations.Add(allocation);
        Context.SaveChanges();
        return allocation;
    }

    public void GrantView(User employee, PermissionModule module, bool canView)
    {
        Context.UserPermissions.Add(new UserPermission
        {
            UserId = employee.Id,
            Module = module,
            CanView = canView,
            CanCreate = false,
            CanModify = false
        });
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
}
