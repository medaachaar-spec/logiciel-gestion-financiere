using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.Payments;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Tests;

public sealed class PaymentObligationReadServiceTests
{
    [Fact]
    public async Task Customer_list_is_one_row_per_obligation_and_uses_explicit_allocations_only()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-obligations", UserRole.Responsable);
        var atlas = database.AddClient("Atlas Distribution");
        var unpaid = database.AddSale(responsible, "VTE-UNPAID", 300_000, atlas.Id);
        var settled = database.AddSale(responsible, "VTE-SETTLED", 300_000, atlas.Id);
        var partial = database.AddSale(responsible, "VTE-PARTIAL", 500_000, atlas.Id);
        var other = database.AddSale(responsible, "VTE-OTHER", 400_000, atlas.Id);
        var initial = database.AddReceivable(responsible, atlas, "CRI-INITIAL", 100_000);

        foreach (var amount in new long[] { 50_000, 100_000, 120_000, 30_000 })
        {
            var payment = database.AddCustomerPayment(
                responsible, $"ENC-FULL-{amount}", new DateOnly(2026, 9, 2), amount, atlas.Id);
            database.AddCustomerAllocation(payment, amount, saleId: settled.Id);
        }

        var partialPayment = database.AddCustomerPayment(
            responsible, "ENC-PARTIAL", new DateOnly(2026, 9, 3), 200_000, atlas.Id);
        database.AddCustomerAllocation(partialPayment, 200_000, saleId: partial.Id);

        var sharedPayment = database.AddCustomerPayment(
            responsible, "ENC-SHARED", new DateOnly(2026, 9, 4), 150_000, atlas.Id);
        database.AddCustomerAllocation(sharedPayment, 50_000, saleId: partial.Id);
        database.AddCustomerAllocation(sharedPayment, 100_000, saleId: other.Id);

        var initialPayment = database.AddCustomerPayment(
            responsible, "ENC-INITIAL", new DateOnly(2026, 9, 5), 40_000, atlas.Id);
        database.AddCustomerAllocation(initialPayment, 40_000, receivableId: initial.Id);
        database.AddCustomerPayment(
            responsible, "ENC-UNAPPLIED", new DateOnly(2026, 9, 6), 75_000, atlas.Id);

        await database.AuthenticateAsync(responsible);
        var result = await database.CustomerObligationReadService.GetPageAsync(new(PageSize: 20));
        var partialDetail = await database.CustomerObligationReadService.GetDetailAsync(
            CustomerPaymentObligationTargetType.Sale, partial.Id);

        Assert.True(result.Success);
        Assert.Equal(5, result.TotalCount);
        Assert.Equal(5, result.Items.Select(item => (item.TargetType, item.TargetId)).Distinct().Count());
        AssertAmounts(result, unpaid.Id, 300_000, 0, 300_000, PaymentObligationStatus.Outstanding);
        AssertAmounts(result, settled.Id, 300_000, 300_000, 0, PaymentObligationStatus.Settled);
        AssertAmounts(result, partial.Id, 500_000, 250_000, 250_000, PaymentObligationStatus.PartiallyPaid);
        AssertAmounts(result, other.Id, 400_000, 100_000, 300_000, PaymentObligationStatus.PartiallyPaid);
        var initialItem = Assert.Single(result.Items, item =>
            item.TargetType == CustomerPaymentObligationTargetType.InitialCustomerReceivable);
        Assert.Equal((100_000, 40_000, 60_000),
            (initialItem.OriginalAmountMinor, initialItem.PaidAmountMinor, initialItem.RemainingAmountMinor));
        var sharedHistory = Assert.Single(
            partialDetail.Item!.History, item => item.PaymentReference == "ENC-SHARED");
        Assert.Equal(50_000, sharedHistory.AllocatedAmountMinor);
        Assert.NotEqual(sharedPayment.AmountMinor, sharedHistory.AllocatedAmountMinor);
    }

    [Fact]
    public async Task Customer_history_calculates_chronological_remaining_with_stable_tie_breaking()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-history", UserRole.Responsable);
        var client = database.AddClient("History Client");
        var sale = database.AddSale(responsible, "VTE-HISTORY", 300_000, client.Id);
        var baseTime = new DateTimeOffset(2026, 9, 7, 8, 0, 0, TimeSpan.Zero);
        var amounts = new long[] { 50_000, 100_000, 120_000, 30_000 };
        for (var index = 0; index < amounts.Length; index++)
        {
            var payment = database.AddCustomerPayment(
                responsible,
                $"ENC-{index + 1}",
                new DateOnly(2026, 9, 7),
                amounts[index],
                client.Id,
                createdAtUtc: baseTime.AddMinutes(index));
            database.AddCustomerAllocation(payment, amounts[index], saleId: sale.Id);
        }

        await database.AuthenticateAsync(responsible);
        var detail = await database.CustomerObligationReadService.GetDetailAsync(
            CustomerPaymentObligationTargetType.Sale, sale.Id);

        Assert.True(detail.Success);
        Assert.NotNull(detail.Item);
        Assert.Equal(new[] { "ENC-1", "ENC-2", "ENC-3", "ENC-4" },
            detail.Item.History.Select(item => item.PaymentReference));
        Assert.Equal(new long[] { 250_000, 150_000, 30_000, 0 },
            detail.Item.History.Select(item => item.RemainingAfterPaymentMinor));
        Assert.Equal(new long[] { 50_000, 150_000, 270_000, 300_000 },
            detail.Item.History.Select(item => item.CumulativePaidAmountMinor));
    }

    [Fact]
    public async Task Customer_filters_search_pagination_and_initial_history_are_correct()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("customer-filters", UserRole.Responsable);
        var atlas = database.AddClient("Atlas Market");
        atlas.Identifier = "CLI-ATLAS";
        database.Context.SaveChanges();
        var sale = database.AddSale(responsible, "VTE-FILTER", 80_000, atlas.Id);
        var other = database.AddSale(responsible, "VTE-OTHER-FILTER", 60_000, atlas.Id);
        var initial = database.AddReceivable(responsible, atlas, "CRI-FILTER", 40_000);
        var initialPayment = database.AddCustomerPayment(
            responsible, "ENC-CRI", new DateOnly(2026, 9, 8), 40_000, atlas.Id);
        database.AddCustomerAllocation(initialPayment, 40_000, receivableId: initial.Id);

        await database.AuthenticateAsync(responsible);
        var settled = await database.CustomerObligationReadService.GetPageAsync(
            new(PageSize: 1, SearchText: " CLI-ATLAS ", Status: PaymentObligationStatus.Settled));
        var reference = await database.CustomerObligationReadService.GetPageAsync(
            new(SearchText: "VTE-FILTER"));
        var detail = await database.CustomerObligationReadService.GetDetailAsync(
            CustomerPaymentObligationTargetType.InitialCustomerReceivable, initial.Id);

        Assert.True(settled.Success);
        Assert.Equal(1, settled.TotalCount);
        Assert.Equal(1, settled.TotalPages);
        Assert.Equal(initial.Id, Assert.Single(settled.Items).TargetId);
        Assert.Equal(sale.Id, Assert.Single(reference.Items).TargetId);
        Assert.DoesNotContain(reference.Items, item => item.TargetId == other.Id);
        Assert.Equal(0, Assert.Single(detail.Item!.History).RemainingAfterPaymentMinor);
    }

    [Fact]
    public async Task Supplier_list_and_history_mirror_customer_semantics()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("supplier-obligations", UserRole.Responsable);
        var supplier = database.AddSupplier("Atlas Fournitures");
        var unpaid = database.AddPurchase(responsible, supplier, "ACH-UNPAID", 400_000);
        var partial = database.AddPurchase(responsible, supplier, "ACH-PARTIAL", 500_000);
        var settled = database.AddPurchase(responsible, supplier, "ACH-SETTLED", 300_000);
        var initial = database.AddPayable(responsible, supplier, "DFI-INITIAL", 90_000);

        var shared = database.AddSupplierPayment(
            responsible, "PAY-SHARED", new DateOnly(2026, 9, 2), 250_000, supplier.Id);
        database.AddSupplierAllocation(shared, 200_000, purchaseId: partial.Id);
        database.AddSupplierAllocation(shared, 50_000, payableId: initial.Id);
        database.AddSupplierPayment(
            responsible, "PAY-UNAPPLIED", new DateOnly(2026, 9, 3), 70_000, supplier.Id);

        var baseTime = new DateTimeOffset(2026, 9, 4, 8, 0, 0, TimeSpan.Zero);
        var installments = new long[] { 50_000, 100_000, 120_000, 30_000 };
        for (var index = 0; index < installments.Length; index++)
        {
            var payment = database.AddSupplierPayment(
                responsible,
                $"PAY-{index + 1}",
                new DateOnly(2026, 9, 4),
                installments[index],
                supplier.Id,
                createdAtUtc: baseTime.AddMinutes(index));
            database.AddSupplierAllocation(payment, installments[index], purchaseId: settled.Id);
        }

        await database.AuthenticateAsync(responsible);
        var list = await database.SupplierObligationReadService.GetPageAsync(new(PageSize: 20));
        var detail = await database.SupplierObligationReadService.GetDetailAsync(
            SupplierPaymentObligationTargetType.Purchase, settled.Id);
        var initialDetail = await database.SupplierObligationReadService.GetDetailAsync(
            SupplierPaymentObligationTargetType.InitialSupplierPayable, initial.Id);

        Assert.True(list.Success);
        Assert.Equal(4, list.TotalCount);
        AssertSupplierAmounts(list, unpaid.Id, 400_000, 0, 400_000, PaymentObligationStatus.Outstanding);
        AssertSupplierAmounts(list, partial.Id, 500_000, 200_000, 300_000, PaymentObligationStatus.PartiallyPaid);
        AssertSupplierAmounts(list, settled.Id, 300_000, 300_000, 0, PaymentObligationStatus.Settled);
        var initialItem = Assert.Single(list.Items, item =>
            item.TargetType == SupplierPaymentObligationTargetType.InitialSupplierPayable);
        Assert.Equal((90_000, 50_000, 40_000),
            (initialItem.OriginalAmountMinor, initialItem.PaidAmountMinor, initialItem.RemainingAmountMinor));
        Assert.Equal(new long[] { 250_000, 150_000, 30_000, 0 },
            detail.Item!.History.Select(item => item.RemainingAfterPaymentMinor));
        var initialAllocation = Assert.Single(initialDetail.Item!.History);
        Assert.Equal(50_000, initialAllocation.AllocatedAmountMinor);
        Assert.NotEqual(shared.AmountMinor, initialAllocation.AllocatedAmountMinor);
    }

    [Fact]
    public async Task Initial_receivables_and_payables_support_partial_and_full_history()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("initial-history", UserRole.Responsable);
        var client = database.AddClient("Initial Client");
        var supplier = database.AddSupplier("Initial Supplier");
        var receivablePartial = database.AddReceivable(responsible, client, "CRI-PARTIAL", 100_000);
        var receivableFull = database.AddReceivable(responsible, client, "CRI-FULL", 120_000);
        var payablePartial = database.AddPayable(responsible, supplier, "DFI-PARTIAL", 140_000);
        var payableFull = database.AddPayable(responsible, supplier, "DFI-FULL", 160_000);

        var customerPartialPayment = database.AddCustomerPayment(
            responsible, "ENC-CRI-P", new DateOnly(2026, 9, 6), 40_000, client.Id);
        database.AddCustomerAllocation(customerPartialPayment, 40_000, receivableId: receivablePartial.Id);
        var customerFullPayment = database.AddCustomerPayment(
            responsible, "ENC-CRI-F", new DateOnly(2026, 9, 7), 120_000, client.Id);
        database.AddCustomerAllocation(customerFullPayment, 120_000, receivableId: receivableFull.Id);
        var supplierPartialPayment = database.AddSupplierPayment(
            responsible, "PAY-DFI-P", new DateOnly(2026, 9, 6), 50_000, supplier.Id);
        database.AddSupplierAllocation(supplierPartialPayment, 50_000, payableId: payablePartial.Id);
        var supplierFullPayment = database.AddSupplierPayment(
            responsible, "PAY-DFI-F", new DateOnly(2026, 9, 7), 160_000, supplier.Id);
        database.AddSupplierAllocation(supplierFullPayment, 160_000, payableId: payableFull.Id);

        await database.AuthenticateAsync(responsible);
        var customerPartial = await database.CustomerObligationReadService.GetDetailAsync(
            CustomerPaymentObligationTargetType.InitialCustomerReceivable, receivablePartial.Id);
        var customerFull = await database.CustomerObligationReadService.GetDetailAsync(
            CustomerPaymentObligationTargetType.InitialCustomerReceivable, receivableFull.Id);
        var supplierPartial = await database.SupplierObligationReadService.GetDetailAsync(
            SupplierPaymentObligationTargetType.InitialSupplierPayable, payablePartial.Id);
        var supplierFull = await database.SupplierObligationReadService.GetDetailAsync(
            SupplierPaymentObligationTargetType.InitialSupplierPayable, payableFull.Id);

        Assert.Equal((40_000L, 60_000L, PaymentObligationStatus.PartiallyPaid),
            (customerPartial.Item!.PaidAmountMinor, customerPartial.Item.RemainingAmountMinor, customerPartial.Item.Status));
        Assert.Equal((120_000L, 0L, PaymentObligationStatus.Settled),
            (customerFull.Item!.PaidAmountMinor, customerFull.Item.RemainingAmountMinor, customerFull.Item.Status));
        Assert.Equal((50_000L, 90_000L, PaymentObligationStatus.PartiallyPaid),
            (supplierPartial.Item!.PaidAmountMinor, supplierPartial.Item.RemainingAmountMinor, supplierPartial.Item.Status));
        Assert.Equal((160_000L, 0L, PaymentObligationStatus.Settled),
            (supplierFull.Item!.PaidAmountMinor, supplierFull.Item.RemainingAmountMinor, supplierFull.Item.Status));
        Assert.Single(customerPartial.Item.History);
        Assert.Single(customerFull.Item.History);
        Assert.Single(supplierPartial.Item.History);
        Assert.Single(supplierFull.Item.History);
    }

    [Fact]
    public async Task View_only_employee_can_read_cross_user_obligations_but_cannot_create()
    {
        using var database = new PaymentReadTestDatabase();
        var owner = database.AddUser("obligation-owner", UserRole.Responsable);
        var employee = database.AddUser("obligation-viewer", UserRole.Employe);
        database.GrantView(employee, PermissionModule.CustomerPayments, canView: true);
        database.GrantView(employee, PermissionModule.SupplierPayments, canView: true);
        var client = database.AddClient("Cross User Client");
        var supplier = database.AddSupplier("Cross User Supplier");
        var sale = database.AddSale(owner, "VTE-CROSS", 10_000, client.Id);
        var purchase = database.AddPurchase(owner, supplier, "ACH-CROSS", 20_000);

        await database.AuthenticateAsync(employee);
        var customerPage = await database.CustomerObligationReadService.GetPageAsync(new());
        var customerDetail = await database.CustomerObligationReadService.GetDetailAsync(
            CustomerPaymentObligationTargetType.Sale, sale.Id);
        var supplierPage = await database.SupplierObligationReadService.GetPageAsync(new());
        var supplierDetail = await database.SupplierObligationReadService.GetDetailAsync(
            SupplierPaymentObligationTargetType.Purchase, purchase.Id);
        var customerCreate = await database.CustomerPaymentService.CreateAsync(
            new CreateCustomerPaymentCommand(client.Id, 1_000, PaymentMethod.Cash, null,
                [new CustomerPaymentAllocationInput(sale.Id, null, 1_000)]));
        var supplierCreate = await database.SupplierPaymentService.CreateAsync(
            new CreateSupplierPaymentCommand(supplier.Id, 1_000, PaymentMethod.Cash, null,
                [new SupplierPaymentAllocationInput(purchase.Id, null, 1_000)]));

        Assert.True(customerPage.Success);
        Assert.True(customerDetail.Success);
        Assert.True(supplierPage.Success);
        Assert.True(supplierDetail.Success);
        Assert.Equal(CreateCustomerPaymentFailureReason.Forbidden, customerCreate.FailureReason);
        Assert.Equal(CreateSupplierPaymentFailureReason.Forbidden, supplierCreate.FailureReason);
    }

    [Fact]
    public async Task Obligation_candidate_validation_query_count_is_batched_as_row_count_grows()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("obligation-query-count", UserRole.Responsable);
        var client = database.AddClient("Query Client");
        database.AddSale(responsible, "VTE-ONE", 10_000, client.Id);
        await database.AuthenticateAsync(responsible);

        database.CommandCounter.Reset();
        var oneRow = await database.CustomerObligationReadService.GetPageAsync(new(PageSize: 50));
        var oneRowCommandCount = database.CommandCounter.CommandCount;

        for (var index = 0; index < 20; index++)
            database.AddSale(responsible, $"VTE-MANY-{index:00}", 10_000, client.Id);

        database.CommandCounter.Reset();
        var manyRows = await database.CustomerObligationReadService.GetPageAsync(new(PageSize: 50));
        var manyRowCommandCount = database.CommandCounter.CommandCount;

        Assert.True(oneRow.Success);
        Assert.True(manyRows.Success);
        Assert.Equal(1, oneRow.TotalCount);
        Assert.Equal(21, manyRows.TotalCount);
        Assert.Equal(3, oneRowCommandCount);
        Assert.Equal(oneRowCommandCount, manyRowCommandCount);
        Assert.Equal(3, manyRowCommandCount);
    }

    [Fact]
    public async Task Typed_target_identity_keeps_colliding_document_and_initial_ids_distinct()
    {
        using var database = new PaymentReadTestDatabase();
        var responsible = database.AddUser("obligation-typed-identity", UserRole.Responsable);
        var client = database.AddClient("Typed customer");
        var supplier = database.AddSupplier("Typed supplier");
        var sale = database.AddSale(responsible, "VTE-ID-1", 10_000, client.Id);
        var receivable = database.AddReceivable(responsible, client, "CRI-ID-1", 20_000);
        var purchase = database.AddPurchase(responsible, supplier, "ACH-ID-1", 30_000);
        var payable = database.AddPayable(responsible, supplier, "DFI-ID-1", 40_000);
        Assert.Equal(sale.Id, receivable.Id);
        Assert.Equal(purchase.Id, payable.Id);

        await database.AuthenticateAsync(responsible);
        var customerSale = await database.CustomerObligationReadService.GetDetailAsync(
            CustomerPaymentObligationTargetType.Sale, sale.Id);
        var customerInitial = await database.CustomerObligationReadService.GetDetailAsync(
            CustomerPaymentObligationTargetType.InitialCustomerReceivable, receivable.Id);
        var supplierPurchase = await database.SupplierObligationReadService.GetDetailAsync(
            SupplierPaymentObligationTargetType.Purchase, purchase.Id);
        var supplierInitial = await database.SupplierObligationReadService.GetDetailAsync(
            SupplierPaymentObligationTargetType.InitialSupplierPayable, payable.Id);

        Assert.Equal("VTE-ID-1", customerSale.Item!.Reference);
        Assert.Equal("CRI-ID-1", customerInitial.Item!.Reference);
        Assert.Equal("ACH-ID-1", supplierPurchase.Item!.Reference);
        Assert.Equal("DFI-ID-1", supplierInitial.Item!.Reference);
    }

    [Fact]
    public async Task Obligation_reads_fail_closed_without_view_permission()
    {
        using var database = new PaymentReadTestDatabase();
        var unauthenticatedCustomer = await database.CustomerObligationReadService.GetPageAsync(new());
        var unauthenticatedSupplier = await database.SupplierObligationReadService.GetPageAsync(new());
        var employee = database.AddUser("obligation-denied", UserRole.Employe);
        database.GrantView(employee, PermissionModule.CustomerPayments, canView: false);
        database.GrantView(employee, PermissionModule.SupplierPayments, canView: false);
        await database.AuthenticateAsync(employee);

        var forbiddenCustomer = await database.CustomerObligationReadService.GetPageAsync(new());
        var forbiddenSupplier = await database.SupplierObligationReadService.GetPageAsync(new());

        Assert.Equal(CustomerPaymentObligationReadFailureReason.NotAuthenticated,
            unauthenticatedCustomer.FailureReason);
        Assert.Equal(SupplierPaymentObligationReadFailureReason.NotAuthenticated,
            unauthenticatedSupplier.FailureReason);
        Assert.Equal(CustomerPaymentObligationReadFailureReason.Forbidden,
            forbiddenCustomer.FailureReason);
        Assert.Equal(SupplierPaymentObligationReadFailureReason.Forbidden,
            forbiddenSupplier.FailureReason);
    }

    private static void AssertAmounts(
        CustomerPaymentObligationPageResult result,
        long saleId,
        long original,
        long paid,
        long remaining,
        PaymentObligationStatus status)
    {
        var item = Assert.Single(result.Items, candidate =>
            candidate.TargetType == CustomerPaymentObligationTargetType.Sale
            && candidate.TargetId == saleId);
        Assert.Equal((original, paid, remaining, status),
            (item.OriginalAmountMinor, item.PaidAmountMinor, item.RemainingAmountMinor, item.Status));
    }

    private static void AssertSupplierAmounts(
        SupplierPaymentObligationPageResult result,
        long purchaseId,
        long original,
        long paid,
        long remaining,
        PaymentObligationStatus status)
    {
        var item = Assert.Single(result.Items, candidate =>
            candidate.TargetType == SupplierPaymentObligationTargetType.Purchase
            && candidate.TargetId == purchaseId);
        Assert.Equal((original, paid, remaining, status),
            (item.OriginalAmountMinor, item.PaidAmountMinor, item.RemainingAmountMinor, item.Status));
    }
}
