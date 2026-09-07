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

public sealed class CustomerPaymentObligationLookupServiceTests
{
    private static readonly DateOnly Day1 = new(2026, 9, 1);
    private static readonly DateOnly Day2 = new(2026, 9, 2);
    private static readonly DateOnly Day3 = new(2026, 9, 3);

    [Fact]
    public async Task UnauthenticatedLookupIsRejectedWithoutData()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var client = database.AddClient("Secret Client");
        var owner = database.AddUser("owner", UserRole.Responsable);
        database.AddSale(owner, client.Id, "VTE-SECRET", Day1, 100);

        var result = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id));

        Assert.False(result.Success);
        Assert.Equal(CustomerPaymentObligationLookupFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task CreatePurposeRequiresCreatePermissionWithoutAcceptingModify()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var employee = database.AddUser("customer-modifier", UserRole.Employe);
        var client = database.AddClient("Client");
        database.Grant(employee, PermissionModule.CustomerPayments, canModify: true);
        await database.AuthenticateAsync(employee);

        var create = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id));
        var advance = await database.CustomerLookup.SearchAsync(AdvanceQuery(client.Id));

        Assert.False(create.Success);
        Assert.Equal(CustomerPaymentObligationLookupFailureReason.Forbidden, create.FailureReason);
        Assert.True(advance.Success);
    }

    [Fact]
    public async Task AdvancePurposeRequiresModifyPermissionWithoutAcceptingCreate()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var employee = database.AddUser("customer-creator", UserRole.Employe);
        var client = database.AddClient("Client");
        database.Grant(employee, PermissionModule.CustomerPayments, canCreate: true);
        await database.AuthenticateAsync(employee);

        var create = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id));
        var advance = await database.CustomerLookup.SearchAsync(AdvanceQuery(client.Id));

        Assert.True(create.Success);
        Assert.False(advance.Success);
        Assert.Equal(CustomerPaymentObligationLookupFailureReason.Forbidden, advance.FailureReason);
    }

    [Fact]
    public async Task UndefinedPurposeReturnsControlledFailure()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Client");
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerLookup.SearchAsync(
            new(client.Id, (CustomerPaymentObligationLookupPurpose)999));

        Assert.False(result.Success);
        Assert.Equal(CustomerPaymentObligationLookupFailureReason.InvalidPurpose, result.FailureReason);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task InvalidClientIdReturnsControlledFailure(long clientId)
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerLookup.SearchAsync(CreateQuery(clientId));

        Assert.False(result.Success);
        Assert.Equal(CustomerPaymentObligationLookupFailureReason.InvalidClientId, result.FailureReason);
    }

    [Fact]
    public async Task MissingClientReturnsControlledFailure()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerLookup.SearchAsync(CreateQuery(999_999));

        Assert.False(result.Success);
        Assert.Equal(CustomerPaymentObligationLookupFailureReason.ClientNotFound, result.FailureReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task InvalidLimitReturnsControlledFailure(int limit)
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Client");
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id, limit: limit));

        Assert.False(result.Success);
        Assert.Equal(CustomerPaymentObligationLookupFailureReason.InvalidLimit, result.FailureReason);
    }

    [Fact]
    public async Task ClientWithoutOutstandingObligationsReturnsEmptySuccess()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Client");
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id));

        Assert.True(result.Success);
        Assert.Equal(CustomerPaymentObligationLookupFailureReason.None, result.FailureReason);
        Assert.Empty(result.Items);
        Assert.Equal(50, result.Limit);
    }

    [Fact]
    public async Task UnpaidSaleReturnsExactSelectionFields()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Client");
        var sale = database.AddSale(responsible, client.Id, "VTE-UNPAID", Day2, 1_250, Day3);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id));

        var item = Assert.Single(result.Items);
        Assert.Equal(CustomerPaymentObligationTargetType.Sale, item.TargetType);
        Assert.Equal(sale.Id, item.TargetId);
        Assert.Equal("VTE-UNPAID", item.Reference);
        Assert.Equal(Day2, item.BusinessDate);
        Assert.Equal(Day3, item.DueDate);
        Assert.Equal(1_250, item.OriginalAmountMinor);
        Assert.Equal(1_250, item.OutstandingAmountMinor);
    }

    [Fact]
    public async Task PartiallyPaidSaleIsDerivedAndFullyPaidSaleIsExcluded()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Client");
        var partial = database.AddSale(responsible, client.Id, "VTE-PARTIAL", Day1, 1_000);
        var settled = database.AddSale(responsible, client.Id, "VTE-SETTLED", Day2, 600);
        var payment = database.AddCustomerPayment(responsible, client.Id, "ENC-1", 1_000);
        database.AddCustomerAllocation(payment, 400, saleId: partial.Id);
        database.AddCustomerAllocation(payment, 600, saleId: settled.Id);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id));

        var item = Assert.Single(result.Items);
        Assert.Equal(partial.Id, item.TargetId);
        Assert.Equal(600, item.OutstandingAmountMinor);
        Assert.DoesNotContain(result.Items, candidate => candidate.TargetId == settled.Id);
    }

    [Fact]
    public async Task InitialReceivableOutstandingIsDerivedAndSettledTargetIsExcluded()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Client");
        var partial = database.AddReceivable(responsible, client.Id, "REC-PARTIAL", Day1, 900);
        var settled = database.AddReceivable(responsible, client.Id, "REC-SETTLED", Day2, 300);
        var payment = database.AddCustomerPayment(responsible, client.Id, "ENC-1", 700);
        database.AddCustomerAllocation(payment, 400, receivableId: partial.Id);
        database.AddCustomerAllocation(payment, 300, receivableId: settled.Id);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id));

        var item = Assert.Single(result.Items);
        Assert.Equal(CustomerPaymentObligationTargetType.InitialCustomerReceivable, item.TargetType);
        Assert.Equal(partial.Id, item.TargetId);
        Assert.Equal(500, item.OutstandingAmountMinor);
        Assert.DoesNotContain(result.Items, candidate => candidate.TargetId == settled.Id);
    }

    [Fact]
    public async Task OtherClientAndAnonymousSalesAreExcluded()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var selected = database.AddClient("Selected");
        var other = database.AddClient("Other");
        var expected = database.AddSale(responsible, selected.Id, "VTE-SELECTED", Day1, 100);
        database.AddSale(responsible, other.Id, "VTE-OTHER", Day1, 100);
        database.AddSale(responsible, null, "VTE-ANONYMOUS", Day1, 100);
        database.AddReceivable(responsible, other.Id, "REC-OTHER", Day1, 100);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerLookup.SearchAsync(CreateQuery(selected.Id));

        Assert.Equal([expected.Id], result.Items.Select(item => item.TargetId));
        Assert.All(result.Items, item => Assert.Equal(CustomerPaymentObligationTargetType.Sale, item.TargetType));
    }

    [Fact]
    public async Task SearchSortingWhitespaceAndLimitAreDeterministic()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Client");
        var saleDay1 = database.AddSale(responsible, client.Id, "MATCH-SALE", Day1, 100);
        var receivableDay1 = database.AddReceivable(responsible, client.Id, "MATCH-REC", Day1, 100);
        database.AddSale(responsible, client.Id, "OTHER", Day2, 100, observation: "MATCH-HIDDEN");
        await database.AuthenticateAsync(responsible);

        var searched = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id, "  MATCH  "));
        var whitespace = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id, "   ", 2));
        var observation = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id, "HIDDEN"));

        Assert.Equal(
            [(CustomerPaymentObligationTargetType.Sale, saleDay1.Id),
             (CustomerPaymentObligationTargetType.InitialCustomerReceivable, receivableDay1.Id)],
            searched.Items.Select(item => (item.TargetType, item.TargetId)));
        Assert.Equal(2, whitespace.Items.Count);
        Assert.Equal(searched.Items.Select(item => item.TargetId), whitespace.Items.Select(item => item.TargetId));
        Assert.Empty(observation.Items);
    }

    [Fact]
    public async Task LongAmountsRemainExact()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Client");
        var sale = database.AddSale(
            responsible,
            client.Id,
            "VTE-LONG",
            Day1,
            9_000_000_000_000_000_000);
        var payment = database.AddCustomerPayment(
            responsible,
            client.Id,
            "ENC-LONG",
            4_000_000_000_000_000_000);
        database.AddCustomerAllocation(payment, 4_000_000_000_000_000_000, saleId: sale.Id);
        await database.AuthenticateAsync(responsible);

        var item = Assert.Single((await database.CustomerLookup.SearchAsync(CreateQuery(client.Id))).Items);

        Assert.Equal(9_000_000_000_000_000_000, item.OriginalAmountMinor);
        Assert.Equal(5_000_000_000_000_000_000, item.OutstandingAmountMinor);
    }

    [Fact]
    public async Task OverallocatedCustomerObligationReturnsDataIntegrityViolation()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Client");
        var sale = database.AddSale(responsible, client.Id, "VTE-CORRUPT", Day1, 100);
        var payment = database.AddCustomerPayment(responsible, client.Id, "ENC-CORRUPT", 101);
        database.AddCustomerAllocation(payment, 101, saleId: sale.Id);
        await database.AuthenticateAsync(responsible);

        var result = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id));

        Assert.False(result.Success);
        Assert.Equal(CustomerPaymentObligationLookupFailureReason.DataIntegrityViolation, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task CustomerLookupDoesNotMutateOrTrackEntities()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var client = database.AddClient("Client");
        var sale = database.AddSale(responsible, client.Id, "VTE-UNCHANGED", Day1, 100);
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.CustomerLookup.SearchAsync(CreateQuery(client.Id));

        Assert.True(result.Success);
        Assert.Empty(database.Context.ChangeTracker.Entries());
        var persisted = await database.Context.Sales.AsNoTracking().SingleAsync(item => item.Id == sale.Id);
        Assert.Equal(100, persisted.TotalAmountMinor);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CustomerLookupPropagatesCancellation()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.CustomerLookup.SearchAsync(CreateQuery(1), cancellation.Token));
    }

    private static CustomerPaymentObligationLookupQuery CreateQuery(
        long clientId,
        string? searchText = null,
        int limit = 50) =>
        new(clientId, CustomerPaymentObligationLookupPurpose.CreatePayment, searchText, limit);

    private static CustomerPaymentObligationLookupQuery AdvanceQuery(long clientId) =>
        new(clientId, CustomerPaymentObligationLookupPurpose.ApplyAdvance);
}

public sealed class SupplierPaymentObligationLookupServiceTests
{
    private static readonly DateOnly Day1 = new(2026, 9, 1);
    private static readonly DateOnly Day2 = new(2026, 9, 2);
    private static readonly DateOnly Day3 = new(2026, 9, 3);

    [Fact]
    public async Task UnauthenticatedLookupIsRejectedWithoutData()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var supplier = database.AddSupplier("Secret Supplier");
        var owner = database.AddUser("owner", UserRole.Responsable);
        database.AddPurchase(owner, supplier.Id, "ACH-SECRET", Day1, 100);

        var result = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id));

        Assert.False(result.Success);
        Assert.Equal(SupplierPaymentObligationLookupFailureReason.NotAuthenticated, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task PurposesRequireTheirExactSupplierPaymentPermission()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var creator = database.AddUser("supplier-creator", UserRole.Employe);
        var supplier = database.AddSupplier("Supplier");
        database.Grant(creator, PermissionModule.SupplierPayments, canCreate: true);
        await database.AuthenticateAsync(creator);

        var create = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id));
        var advance = await database.SupplierLookup.SearchAsync(AdvanceQuery(supplier.Id));

        Assert.True(create.Success);
        Assert.False(advance.Success);
        Assert.Equal(SupplierPaymentObligationLookupFailureReason.Forbidden, advance.FailureReason);
    }

    [Fact]
    public async Task ModifyPermissionSupportsAdvancePurposeWithoutSupportingCreate()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var modifier = database.AddUser("supplier-modifier", UserRole.Employe);
        var supplier = database.AddSupplier("Supplier");
        database.Grant(modifier, PermissionModule.SupplierPayments, canModify: true);
        await database.AuthenticateAsync(modifier);

        var create = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id));
        var advance = await database.SupplierLookup.SearchAsync(AdvanceQuery(supplier.Id));

        Assert.False(create.Success);
        Assert.Equal(SupplierPaymentObligationLookupFailureReason.Forbidden, create.FailureReason);
        Assert.True(advance.Success);
    }

    [Fact]
    public async Task UndefinedPurposeReturnsControlledFailure()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier");
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookup.SearchAsync(
            new(supplier.Id, (SupplierPaymentObligationLookupPurpose)999));

        Assert.False(result.Success);
        Assert.Equal(SupplierPaymentObligationLookupFailureReason.InvalidPurpose, result.FailureReason);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    public async Task InvalidSupplierIdReturnsControlledFailure(long supplierId)
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookup.SearchAsync(CreateQuery(supplierId));

        Assert.False(result.Success);
        Assert.Equal(SupplierPaymentObligationLookupFailureReason.InvalidSupplierId, result.FailureReason);
    }

    [Fact]
    public async Task MissingSupplierReturnsControlledFailure()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookup.SearchAsync(CreateQuery(999_999));

        Assert.False(result.Success);
        Assert.Equal(SupplierPaymentObligationLookupFailureReason.SupplierNotFound, result.FailureReason);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(201)]
    public async Task InvalidLimitReturnsControlledFailure(int limit)
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier");
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id, limit: limit));

        Assert.False(result.Success);
        Assert.Equal(SupplierPaymentObligationLookupFailureReason.InvalidLimit, result.FailureReason);
    }

    [Fact]
    public async Task SupplierWithoutOutstandingObligationsReturnsEmptySuccess()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier");
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id));

        Assert.True(result.Success);
        Assert.Empty(result.Items);
        Assert.Equal(50, result.Limit);
    }

    [Fact]
    public async Task UnpaidPurchaseReturnsExactSelectionFields()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier");
        var purchase = database.AddPurchase(responsible, supplier.Id, "ACH-UNPAID", Day2, 1_250, Day3);
        await database.AuthenticateAsync(responsible);

        var item = Assert.Single((await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id))).Items);

        Assert.Equal(SupplierPaymentObligationTargetType.Purchase, item.TargetType);
        Assert.Equal(purchase.Id, item.TargetId);
        Assert.Equal("ACH-UNPAID", item.Reference);
        Assert.Equal(Day2, item.BusinessDate);
        Assert.Equal(Day3, item.DueDate);
        Assert.Equal(1_250, item.OriginalAmountMinor);
        Assert.Equal(1_250, item.OutstandingAmountMinor);
    }

    [Fact]
    public async Task PartialPurchaseIsDerivedAndSettledPurchaseIsExcluded()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier");
        var partial = database.AddPurchase(responsible, supplier.Id, "ACH-PARTIAL", Day1, 1_000);
        var settled = database.AddPurchase(responsible, supplier.Id, "ACH-SETTLED", Day2, 600);
        var payment = database.AddSupplierPayment(responsible, supplier.Id, "DEC-1", 1_000);
        database.AddSupplierAllocation(payment, 400, purchaseId: partial.Id);
        database.AddSupplierAllocation(payment, 600, purchaseId: settled.Id);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id));

        var item = Assert.Single(result.Items);
        Assert.Equal(partial.Id, item.TargetId);
        Assert.Equal(600, item.OutstandingAmountMinor);
        Assert.DoesNotContain(result.Items, candidate => candidate.TargetId == settled.Id);
    }

    [Fact]
    public async Task InitialPayableOutstandingIsDerivedAndSettledTargetIsExcluded()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier");
        var partial = database.AddPayable(responsible, supplier.Id, "PAY-PARTIAL", Day1, 900);
        var settled = database.AddPayable(responsible, supplier.Id, "PAY-SETTLED", Day2, 300);
        var payment = database.AddSupplierPayment(responsible, supplier.Id, "DEC-1", 700);
        database.AddSupplierAllocation(payment, 400, payableId: partial.Id);
        database.AddSupplierAllocation(payment, 300, payableId: settled.Id);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id));

        var item = Assert.Single(result.Items);
        Assert.Equal(SupplierPaymentObligationTargetType.InitialSupplierPayable, item.TargetType);
        Assert.Equal(partial.Id, item.TargetId);
        Assert.Equal(500, item.OutstandingAmountMinor);
        Assert.DoesNotContain(result.Items, candidate => candidate.TargetId == settled.Id);
    }

    [Fact]
    public async Task OtherSupplierTargetsAreExcluded()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var selected = database.AddSupplier("Selected");
        var other = database.AddSupplier("Other");
        var expected = database.AddPurchase(responsible, selected.Id, "ACH-SELECTED", Day1, 100);
        database.AddPurchase(responsible, other.Id, "ACH-OTHER", Day1, 100);
        database.AddPayable(responsible, other.Id, "PAY-OTHER", Day1, 100);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookup.SearchAsync(CreateQuery(selected.Id));

        Assert.Equal([expected.Id], result.Items.Select(item => item.TargetId));
        Assert.All(result.Items, item => Assert.Equal(SupplierPaymentObligationTargetType.Purchase, item.TargetType));
    }

    [Fact]
    public async Task SearchSortingWhitespaceAndLimitAreDeterministic()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier");
        var purchaseDay1 = database.AddPurchase(responsible, supplier.Id, "MATCH-PURCHASE", Day1, 100);
        var payableDay1 = database.AddPayable(responsible, supplier.Id, "MATCH-PAYABLE", Day1, 100);
        database.AddPurchase(responsible, supplier.Id, "OTHER", Day2, 100, observation: "MATCH-HIDDEN");
        await database.AuthenticateAsync(responsible);

        var searched = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id, " MATCH "));
        var whitespace = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id, "   ", 2));
        var observation = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id, "HIDDEN"));

        Assert.Equal(
            [(SupplierPaymentObligationTargetType.Purchase, purchaseDay1.Id),
             (SupplierPaymentObligationTargetType.InitialSupplierPayable, payableDay1.Id)],
            searched.Items.Select(item => (item.TargetType, item.TargetId)));
        Assert.Equal(2, whitespace.Items.Count);
        Assert.Equal(searched.Items.Select(item => item.TargetId), whitespace.Items.Select(item => item.TargetId));
        Assert.Empty(observation.Items);
    }

    [Fact]
    public async Task LongAmountsRemainExact()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier");
        var purchase = database.AddPurchase(
            responsible,
            supplier.Id,
            "ACH-LONG",
            Day1,
            9_000_000_000_000_000_000);
        var payment = database.AddSupplierPayment(
            responsible,
            supplier.Id,
            "DEC-LONG",
            4_000_000_000_000_000_000);
        database.AddSupplierAllocation(payment, 4_000_000_000_000_000_000, purchaseId: purchase.Id);
        await database.AuthenticateAsync(responsible);

        var item = Assert.Single((await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id))).Items);

        Assert.Equal(9_000_000_000_000_000_000, item.OriginalAmountMinor);
        Assert.Equal(5_000_000_000_000_000_000, item.OutstandingAmountMinor);
    }

    [Fact]
    public async Task OverallocatedSupplierObligationReturnsDataIntegrityViolation()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier");
        var purchase = database.AddPurchase(responsible, supplier.Id, "ACH-CORRUPT", Day1, 100);
        var payment = database.AddSupplierPayment(responsible, supplier.Id, "DEC-CORRUPT", 101);
        database.AddSupplierAllocation(payment, 101, purchaseId: purchase.Id);
        await database.AuthenticateAsync(responsible);

        var result = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id));

        Assert.False(result.Success);
        Assert.Equal(SupplierPaymentObligationLookupFailureReason.DataIntegrityViolation, result.FailureReason);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task SupplierLookupDoesNotMutateOrTrackEntities()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        var responsible = database.AddUser("responsable", UserRole.Responsable);
        var supplier = database.AddSupplier("Supplier");
        var purchase = database.AddPurchase(responsible, supplier.Id, "ACH-UNCHANGED", Day1, 100);
        await database.AuthenticateAsync(responsible);
        database.Context.ChangeTracker.Clear();

        var result = await database.SupplierLookup.SearchAsync(CreateQuery(supplier.Id));

        Assert.True(result.Success);
        Assert.Empty(database.Context.ChangeTracker.Entries());
        var persisted = await database.Context.Purchases.AsNoTracking().SingleAsync(item => item.Id == purchase.Id);
        Assert.Equal(100, persisted.TotalAmountMinor);
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SupplierLookupPropagatesCancellation()
    {
        using var database = new PaymentObligationLookupTestDatabase();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SupplierLookup.SearchAsync(CreateQuery(1), cancellation.Token));
    }

    private static SupplierPaymentObligationLookupQuery CreateQuery(
        long supplierId,
        string? searchText = null,
        int limit = 50) =>
        new(supplierId, SupplierPaymentObligationLookupPurpose.CreatePayment, searchText, limit);

    private static SupplierPaymentObligationLookupQuery AdvanceQuery(long supplierId) =>
        new(supplierId, SupplierPaymentObligationLookupPurpose.ApplyAdvance);
}

internal sealed class PaymentObligationLookupTestDatabase : IDisposable
{
    private const string Password = "Payment obligation lookup test password 42!";
    private static readonly DateTimeOffset TestUtc =
        new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    private readonly string databasePath;
    private readonly ServiceProvider serviceProvider;
    private readonly IServiceScope scope;
    private readonly IPasswordHasher passwordHasher;
    private readonly IAuthenticationService authenticationService;

    public PaymentObligationLookupTestDatabase()
    {
        databasePath = Path.Combine(
            Path.GetTempPath(),
            $"FinancialManagement-PaymentObligationLookup-{Guid.NewGuid():N}.db");
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
        CustomerLookup = scope.ServiceProvider
            .GetRequiredService<ICustomerPaymentObligationLookupService>();
        SupplierLookup = scope.ServiceProvider
            .GetRequiredService<ISupplierPaymentObligationLookupService>();
    }

    public FinancialManagementDbContext Context { get; }

    public ICustomerPaymentObligationLookupService CustomerLookup { get; }

    public ISupplierPaymentObligationLookupService SupplierLookup { get; }

    public User AddUser(string username, UserRole role)
    {
        var user = new User
        {
            FullName = $"Payment Obligation Lookup Test {username}",
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

    public void Grant(
        User user,
        PermissionModule module,
        bool canCreate = false,
        bool canModify = false)
    {
        Context.UserPermissions.Add(new UserPermission
        {
            UserId = user.Id,
            Module = module,
            CanView = true,
            CanCreate = canCreate,
            CanModify = canModify
        });
        Context.SaveChanges();
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
        long? clientId,
        string reference,
        DateOnly businessDate,
        long amountMinor,
        DateOnly? dueDate = null,
        string? observation = null)
    {
        var sale = new Sale
        {
            Reference = reference,
            SaleDate = businessDate,
            TotalAmountMinor = amountMinor,
            ClientId = clientId,
            DueDate = dueDate,
            Observation = observation,
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
        long clientId,
        string reference,
        DateOnly businessDate,
        long amountMinor,
        DateOnly? dueDate = null)
    {
        var receivable = new InitialCustomerReceivable
        {
            Reference = reference,
            ClientId = clientId,
            ReceivableDate = businessDate,
            InitialAmountMinor = amountMinor,
            DueDate = dueDate,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc
        };
        Context.InitialCustomerReceivables.Add(receivable);
        Context.SaveChanges();
        return receivable;
    }

    public Purchase AddPurchase(
        User creator,
        long supplierId,
        string reference,
        DateOnly businessDate,
        long amountMinor,
        DateOnly? dueDate = null,
        string? observation = null)
    {
        var purchase = new Purchase
        {
            Reference = reference,
            SupplierId = supplierId,
            PurchaseDate = businessDate,
            TotalAmountMinor = amountMinor,
            DueDate = dueDate,
            Observation = observation,
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
        long supplierId,
        string reference,
        DateOnly businessDate,
        long amountMinor,
        DateOnly? dueDate = null)
    {
        var payable = new InitialSupplierPayable
        {
            Reference = reference,
            SupplierId = supplierId,
            PayableDate = businessDate,
            InitialAmountMinor = amountMinor,
            DueDate = dueDate,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc
        };
        Context.InitialSupplierPayables.Add(payable);
        Context.SaveChanges();
        return payable;
    }

    public CustomerPayment AddCustomerPayment(
        User creator,
        long? clientId,
        string reference,
        long amountMinor)
    {
        var payment = new CustomerPayment
        {
            Reference = reference,
            ClientId = clientId,
            PaymentDate = new DateOnly(2026, 9, 7),
            AmountMinor = amountMinor,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc
        };
        Context.CustomerPayments.Add(payment);
        Context.SaveChanges();
        return payment;
    }

    public SupplierPayment AddSupplierPayment(
        User creator,
        long supplierId,
        string reference,
        long amountMinor)
    {
        var payment = new SupplierPayment
        {
            Reference = reference,
            SupplierId = supplierId,
            PaymentDate = new DateOnly(2026, 9, 7),
            AmountMinor = amountMinor,
            PaymentMethod = PaymentMethod.Cash,
            CreatedByUserId = creator.Id,
            CreatedAtUtc = TestUtc
        };
        Context.SupplierPayments.Add(payment);
        Context.SaveChanges();
        return payment;
    }

    public void AddCustomerAllocation(
        CustomerPayment payment,
        long amountMinor,
        long? saleId = null,
        long? receivableId = null)
    {
        Context.CustomerPaymentAllocations.Add(new CustomerPaymentAllocation
        {
            CustomerPaymentId = payment.Id,
            SaleId = saleId,
            InitialCustomerReceivableId = receivableId,
            AmountMinor = amountMinor,
            CreatedAtUtc = TestUtc
        });
        Context.SaveChanges();
    }

    public void AddSupplierAllocation(
        SupplierPayment payment,
        long amountMinor,
        long? purchaseId = null,
        long? payableId = null)
    {
        Context.SupplierPaymentAllocations.Add(new SupplierPaymentAllocation
        {
            SupplierPaymentId = payment.Id,
            PurchaseId = purchaseId,
            InitialSupplierPayableId = payableId,
            AmountMinor = amountMinor,
            CreatedAtUtc = TestUtc
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
