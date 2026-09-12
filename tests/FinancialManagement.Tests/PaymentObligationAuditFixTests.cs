using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.Payments;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;

namespace FinancialManagement.Tests;

public sealed class PaymentObligationAuditFixTests
{
    [Theory]
    [InlineData(true, PaymentObligationStatus.Outstanding)]
    [InlineData(true, PaymentObligationStatus.PartiallyPaid)]
    [InlineData(true, PaymentObligationStatus.Settled)]
    [InlineData(false, PaymentObligationStatus.Outstanding)]
    [InlineData(false, PaymentObligationStatus.PartiallyPaid)]
    [InlineData(false, PaymentObligationStatus.Settled)]
    public async Task Every_status_filter_rejects_an_overallocated_candidate(
        bool customer,
        PaymentObligationStatus status)
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser($"status-corruption-{customer}-{status}", UserRole.Responsable);

        if (customer)
        {
            var client = database.AddClient("Overallocated customer");
            var sale = database.AddSale(user, "VTE-OVER", 1_000, client.Id);
            var payment = database.AddCustomerPayment(user, "ENC-OVER", Date(1), 1_200, client.Id);
            database.AddCustomerAllocation(payment, 1_200, saleId: sale.Id);
            await database.AuthenticateAsync(user);

            var result = await database.CustomerObligationReadService.GetPageAsync(new(Status: status));

            Assert.Equal(CustomerPaymentObligationReadFailureReason.DataIntegrityViolation, result.FailureReason);
            Assert.Empty(result.Items);
        }
        else
        {
            var supplier = database.AddSupplier("Overallocated supplier");
            var purchase = database.AddPurchase(user, supplier, "ACH-OVER", 1_000);
            var payment = database.AddSupplierPayment(user, "PAY-OVER", Date(1), 1_200, supplier.Id);
            database.AddSupplierAllocation(payment, 1_200, purchaseId: purchase.Id);
            await database.AuthenticateAsync(user);

            var result = await database.SupplierObligationReadService.GetPageAsync(new(Status: status));

            Assert.Equal(SupplierPaymentObligationReadFailureReason.DataIntegrityViolation, result.FailureReason);
            Assert.Empty(result.Items);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Cross_party_allocations_fail_list_and_detail_reads(bool customer)
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser($"cross-party-{customer}", UserRole.Responsable);

        if (customer)
        {
            var targetClient = database.AddClient("Target customer");
            var paymentClient = database.AddClient("Payment customer");
            var sale = database.AddSale(user, "VTE-CROSS-PARTY", 1_000, targetClient.Id);
            var payment = database.AddCustomerPayment(user, "ENC-CROSS-PARTY", Date(1), 400, paymentClient.Id);
            database.AddCustomerAllocation(payment, 400, saleId: sale.Id);
            await database.AuthenticateAsync(user);

            var page = await database.CustomerObligationReadService.GetPageAsync(new());
            var detail = await database.CustomerObligationReadService.GetDetailAsync(
                CustomerPaymentObligationTargetType.Sale, sale.Id);

            AssertCustomerIntegrityFailure(page, detail);
        }
        else
        {
            var targetSupplier = database.AddSupplier("Target supplier");
            var paymentSupplier = database.AddSupplier("Payment supplier");
            var purchase = database.AddPurchase(user, targetSupplier, "ACH-CROSS-PARTY", 1_000);
            var payment = database.AddSupplierPayment(user, "PAY-CROSS-PARTY", Date(1), 400, paymentSupplier.Id);
            database.AddSupplierAllocation(payment, 400, purchaseId: purchase.Id);
            await database.AuthenticateAsync(user);

            var page = await database.SupplierObligationReadService.GetPageAsync(new());
            var detail = await database.SupplierObligationReadService.GetDetailAsync(
                SupplierPaymentObligationTargetType.Purchase, purchase.Id);

            AssertSupplierIntegrityFailure(page, detail);
        }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task Invalid_payment_method_or_reference_fails_list_and_detail_reads(
        bool customer,
        bool invalidMethod)
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser($"metadata-corruption-{customer}-{invalidMethod}", UserRole.Responsable);

        if (customer)
        {
            var client = database.AddClient("Metadata customer");
            var sale = database.AddSale(user, "VTE-METADATA", 1_000, client.Id);
            var payment = database.AddCustomerPayment(user, "ENC-METADATA", Date(1), 400, client.Id);
            database.AddCustomerAllocation(payment, 400, saleId: sale.Id);
            database.ExecuteExternally(invalidMethod
                ? $"UPDATE CustomerPayments SET PaymentMethod = 999 WHERE Id = {payment.Id};"
                : $"UPDATE CustomerPayments SET Reference = ' ' WHERE Id = {payment.Id};");
            await database.AuthenticateAsync(user);

            var page = await database.CustomerObligationReadService.GetPageAsync(new());
            var detail = await database.CustomerObligationReadService.GetDetailAsync(
                CustomerPaymentObligationTargetType.Sale, sale.Id);

            AssertCustomerIntegrityFailure(page, detail);
        }
        else
        {
            var supplier = database.AddSupplier("Metadata supplier");
            var purchase = database.AddPurchase(user, supplier, "ACH-METADATA", 1_000);
            var payment = database.AddSupplierPayment(user, "PAY-METADATA", Date(1), 400, supplier.Id);
            database.AddSupplierAllocation(payment, 400, purchaseId: purchase.Id);
            database.ExecuteExternally(invalidMethod
                ? $"UPDATE SupplierPayments SET PaymentMethod = 999 WHERE Id = {payment.Id};"
                : $"UPDATE SupplierPayments SET Reference = ' ' WHERE Id = {payment.Id};");
            await database.AuthenticateAsync(user);

            var page = await database.SupplierObligationReadService.GetPageAsync(new());
            var detail = await database.SupplierObligationReadService.GetDetailAsync(
                SupplierPaymentObligationTargetType.Purchase, purchase.Id);

            AssertSupplierIntegrityFailure(page, detail);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Allocation_totals_above_long_max_fail_without_sql_overflow(bool customer)
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser($"overflow-{customer}", UserRole.Responsable);

        if (customer)
        {
            var client = database.AddClient("Overflow customer");
            var sale = database.AddSale(user, "VTE-OVERFLOW", long.MaxValue, client.Id);
            var first = database.AddCustomerPayment(user, "ENC-MAX", Date(1), long.MaxValue, client.Id);
            var second = database.AddCustomerPayment(user, "ENC-ONE", Date(2), 1, client.Id);
            database.AddCustomerAllocation(first, long.MaxValue, saleId: sale.Id);
            database.AddCustomerAllocation(second, 1, saleId: sale.Id);
            await database.AuthenticateAsync(user);

            var result = await database.CustomerObligationReadService.GetPageAsync(new());

            Assert.Equal(CustomerPaymentObligationReadFailureReason.DataIntegrityViolation, result.FailureReason);
        }
        else
        {
            var supplier = database.AddSupplier("Overflow supplier");
            var purchase = database.AddPurchase(user, supplier, "ACH-OVERFLOW", long.MaxValue);
            var first = database.AddSupplierPayment(user, "PAY-MAX", Date(1), long.MaxValue, supplier.Id);
            var second = database.AddSupplierPayment(user, "PAY-ONE", Date(2), 1, supplier.Id);
            database.AddSupplierAllocation(first, long.MaxValue, purchaseId: purchase.Id);
            database.AddSupplierAllocation(second, 1, purchaseId: purchase.Id);
            await database.AuthenticateAsync(user);

            var result = await database.SupplierObligationReadService.GetPageAsync(new());

            Assert.Equal(SupplierPaymentObligationReadFailureReason.DataIntegrityViolation, result.FailureReason);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Allocation_total_equal_to_long_max_remains_readable(bool customer)
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser($"max-total-{customer}", UserRole.Responsable);

        if (customer)
        {
            var client = database.AddClient("Maximum customer");
            var sale = database.AddSale(user, "VTE-MAX", long.MaxValue, client.Id);
            var payment = database.AddCustomerPayment(user, "ENC-MAX", Date(1), long.MaxValue, client.Id);
            database.AddCustomerAllocation(payment, long.MaxValue, saleId: sale.Id);
            await database.AuthenticateAsync(user);

            var result = await database.CustomerObligationReadService.GetPageAsync(new());

            Assert.True(result.Success);
            Assert.Equal(0, Assert.Single(result.Items).RemainingAmountMinor);
        }
        else
        {
            var supplier = database.AddSupplier("Maximum supplier");
            var purchase = database.AddPurchase(user, supplier, "ACH-MAX", long.MaxValue);
            var payment = database.AddSupplierPayment(user, "PAY-MAX", Date(1), long.MaxValue, supplier.Id);
            database.AddSupplierAllocation(payment, long.MaxValue, purchaseId: purchase.Id);
            await database.AuthenticateAsync(user);

            var result = await database.SupplierObligationReadService.GetPageAsync(new());

            Assert.True(result.Success);
            Assert.Equal(0, Assert.Single(result.Items).RemainingAmountMinor);
        }
    }

    [Theory]
    [InlineData("customer-sale")]
    [InlineData("customer-initial")]
    [InlineData("supplier-purchase")]
    [InlineData("supplier-initial")]
    public async Task Missing_required_party_fails_list_and_detail_reads(string target)
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser($"orphan-{target}", UserRole.Responsable);

        if (target == "customer-sale")
        {
            var client = database.AddClient("Orphan sale customer");
            var sale = database.AddSale(user, "VTE-ORPHAN", 1_000, client.Id);
            database.ExecuteExternally($"DELETE FROM Clients WHERE Id = {client.Id};");
            await database.AuthenticateAsync(user);
            AssertCustomerIntegrityFailure(
                await database.CustomerObligationReadService.GetPageAsync(new()),
                await database.CustomerObligationReadService.GetDetailAsync(
                    CustomerPaymentObligationTargetType.Sale, sale.Id));
        }
        else if (target == "customer-initial")
        {
            var client = database.AddClient("Orphan receivable customer");
            var receivable = database.AddReceivable(user, client, "CRI-ORPHAN", 1_000);
            database.ExecuteExternally($"DELETE FROM Clients WHERE Id = {client.Id};");
            await database.AuthenticateAsync(user);
            AssertCustomerIntegrityFailure(
                await database.CustomerObligationReadService.GetPageAsync(new()),
                await database.CustomerObligationReadService.GetDetailAsync(
                    CustomerPaymentObligationTargetType.InitialCustomerReceivable, receivable.Id));
        }
        else if (target == "supplier-purchase")
        {
            var supplier = database.AddSupplier("Orphan purchase supplier");
            var purchase = database.AddPurchase(user, supplier, "ACH-ORPHAN", 1_000);
            database.ExecuteExternally($"DELETE FROM Suppliers WHERE Id = {supplier.Id};");
            await database.AuthenticateAsync(user);
            AssertSupplierIntegrityFailure(
                await database.SupplierObligationReadService.GetPageAsync(new()),
                await database.SupplierObligationReadService.GetDetailAsync(
                    SupplierPaymentObligationTargetType.Purchase, purchase.Id));
        }
        else
        {
            var supplier = database.AddSupplier("Orphan payable supplier");
            var payable = database.AddPayable(user, supplier, "DFI-ORPHAN", 1_000);
            database.ExecuteExternally($"DELETE FROM Suppliers WHERE Id = {supplier.Id};");
            await database.AuthenticateAsync(user);
            AssertSupplierIntegrityFailure(
                await database.SupplierObligationReadService.GetPageAsync(new()),
                await database.SupplierObligationReadService.GetDetailAsync(
                    SupplierPaymentObligationTargetType.InitialSupplierPayable, payable.Id));
        }
    }

    [Fact]
    public async Task Anonymous_sale_with_anonymous_payment_remains_valid()
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser("anonymous-obligation", UserRole.Responsable);
        var sale = database.AddSale(user, "VTE-ANONYMOUS", 1_000, clientId: null);
        var payment = database.AddCustomerPayment(user, "ENC-ANONYMOUS", Date(1), 1_000, clientId: null);
        database.AddCustomerAllocation(payment, 1_000, saleId: sale.Id);
        await database.AuthenticateAsync(user);

        var page = await database.CustomerObligationReadService.GetPageAsync(new());
        var detail = await database.CustomerObligationReadService.GetDetailAsync(
            CustomerPaymentObligationTargetType.Sale, sale.Id);

        Assert.True(page.Success);
        Assert.Null(Assert.Single(page.Items).ClientId);
        Assert.True(detail.Success);
        Assert.Equal(0, detail.Item!.RemainingAmountMinor);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Repeated_rows_from_one_payment_to_one_target_are_summed_safely(bool customer)
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser($"repeated-allocation-{customer}", UserRole.Responsable);

        if (customer)
        {
            var client = database.AddClient("Repeated customer");
            var sale = database.AddSale(user, "VTE-REPEATED", 1_000, client.Id);
            var payment = database.AddCustomerPayment(user, "ENC-REPEATED", Date(1), 1_000, client.Id);
            database.AddCustomerAllocation(payment, 400, saleId: sale.Id);
            database.AddCustomerAllocation(payment, 600, saleId: sale.Id);
            await database.AuthenticateAsync(user);

            var detail = await database.CustomerObligationReadService.GetDetailAsync(
                CustomerPaymentObligationTargetType.Sale, sale.Id);

            Assert.True(detail.Success);
            Assert.Equal(1_000, detail.Item!.PaidAmountMinor);
            Assert.Equal(2, detail.Item.History.Count);
        }
        else
        {
            var supplier = database.AddSupplier("Repeated supplier");
            var purchase = database.AddPurchase(user, supplier, "ACH-REPEATED", 1_000);
            var payment = database.AddSupplierPayment(user, "PAY-REPEATED", Date(1), 1_000, supplier.Id);
            database.AddSupplierAllocation(payment, 400, purchaseId: purchase.Id);
            database.AddSupplierAllocation(payment, 600, purchaseId: purchase.Id);
            await database.AuthenticateAsync(user);

            var detail = await database.SupplierObligationReadService.GetDetailAsync(
                SupplierPaymentObligationTargetType.Purchase, purchase.Id);

            Assert.True(detail.Success);
            Assert.Equal(1_000, detail.Item!.PaidAmountMinor);
            Assert.Equal(2, detail.Item.History.Count);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Page_read_uses_one_snapshot_when_an_allocation_commits_mid_read(bool customer)
    {
        var interceptor = new SnapshotMutationInterceptor();
        using var database = new PaymentReadTestDatabase(interceptor);
        var user = database.AddUser($"page-snapshot-{customer}", UserRole.Responsable);
        long targetId;
        long paymentId;

        if (customer)
        {
            var client = database.AddClient("Snapshot customer");
            var sale = database.AddSale(user, "VTE-SNAPSHOT", 1_000, client.Id);
            var payment = database.AddCustomerPayment(user, "ENC-SNAPSHOT", Date(1), 1_000, client.Id);
            targetId = sale.Id;
            paymentId = payment.Id;
        }
        else
        {
            var supplier = database.AddSupplier("Snapshot supplier");
            var purchase = database.AddPurchase(user, supplier, "ACH-SNAPSHOT", 1_000);
            var payment = database.AddSupplierPayment(user, "PAY-SNAPSHOT", Date(1), 1_000, supplier.Id);
            targetId = purchase.Id;
            paymentId = payment.Id;
        }

        await database.AuthenticateAsync(user);

        interceptor.Start(() =>
        {
            if (customer)
            {
                database.AddCustomerAllocationExternally(paymentId, targetId, 1_000);
            }
            else
            {
                database.AddSupplierAllocationExternally(paymentId, targetId, 1_000);
            }
        });

        if (customer)
        {
            var page = await database.CustomerObligationReadService.GetPageAsync(
                new(Status: PaymentObligationStatus.Outstanding));

            var item = Assert.Single(page.Items);

            Assert.True(page.Success);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(0, item.PaidAmountMinor);
            Assert.Equal(PaymentObligationStatus.Outstanding, item.Status);
        }
        else
        {
            var page = await database.SupplierObligationReadService.GetPageAsync(
                new(Status: PaymentObligationStatus.Outstanding));

            var item = Assert.Single(page.Items);

            Assert.True(page.Success);
            Assert.Equal(1, page.TotalCount);
            Assert.Equal(0, item.PaidAmountMinor);
            Assert.Equal(PaymentObligationStatus.Outstanding, item.Status);
        }

        Assert.True(interceptor.Mutated);
        Assert.NotEmpty(interceptor.TransactionalReads);
        Assert.All(interceptor.TransactionalReads, Assert.True);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Detail_read_uses_one_snapshot_when_an_allocation_commits_mid_read(bool customer)
    {
        var interceptor = new SnapshotMutationInterceptor();
        using var database = new PaymentReadTestDatabase(interceptor);
        var user = database.AddUser($"detail-snapshot-{customer}", UserRole.Responsable);
        long targetId;
        long paymentId;

        if (customer)
        {
            var client = database.AddClient("Detail snapshot customer");
            var sale = database.AddSale(user, "VTE-DETAIL-SNAPSHOT", 1_000, client.Id);
            var payment = database.AddCustomerPayment(user, "ENC-DETAIL-SNAPSHOT", Date(1), 1_000, client.Id);
            targetId = sale.Id;
            paymentId = payment.Id;
        }
        else
        {
            var supplier = database.AddSupplier("Detail snapshot supplier");
            var purchase = database.AddPurchase(user, supplier, "ACH-DETAIL-SNAPSHOT", 1_000);
            var payment = database.AddSupplierPayment(user, "PAY-DETAIL-SNAPSHOT", Date(1), 1_000, supplier.Id);
            targetId = purchase.Id;
            paymentId = payment.Id;
        }

        await database.AuthenticateAsync(user);

        interceptor.Start(() =>
        {
            if (customer)
            {
                database.AddCustomerAllocationExternally(paymentId, targetId, 1_000);
            }
            else
            {
                database.AddSupplierAllocationExternally(paymentId, targetId, 1_000);
            }
        });

        if (customer)
        {
            var detail = await database.CustomerObligationReadService.GetDetailAsync(
                CustomerPaymentObligationTargetType.Sale,
                targetId);

            Assert.True(detail.Success);
            Assert.Equal(0, detail.Item!.PaidAmountMinor);
            Assert.Empty(detail.Item.History);
        }
        else
        {
            var detail = await database.SupplierObligationReadService.GetDetailAsync(
                SupplierPaymentObligationTargetType.Purchase,
                targetId);

            Assert.True(detail.Success);
            Assert.Equal(0, detail.Item!.PaidAmountMinor);
            Assert.Empty(detail.Item.History);
        }

        Assert.True(interceptor.Mutated);
        Assert.NotEmpty(interceptor.TransactionalReads);
        Assert.All(interceptor.TransactionalReads, Assert.True);
    }

    private static void AssertCustomerIntegrityFailure(
        CustomerPaymentObligationPageResult page,
        CustomerPaymentObligationDetailResult detail)
    {
        Assert.Equal(
            CustomerPaymentObligationReadFailureReason.DataIntegrityViolation,
            page.FailureReason);

        Assert.Empty(page.Items);

        Assert.Equal(
            CustomerPaymentObligationReadFailureReason.DataIntegrityViolation,
            detail.FailureReason);

        Assert.Null(detail.Item);
    }

    private static void AssertSupplierIntegrityFailure(
        SupplierPaymentObligationPageResult page,
        SupplierPaymentObligationDetailResult detail)
    {
        Assert.Equal(
            SupplierPaymentObligationReadFailureReason.DataIntegrityViolation,
            page.FailureReason);

        Assert.Empty(page.Items);

        Assert.Equal(
            SupplierPaymentObligationReadFailureReason.DataIntegrityViolation,
            detail.FailureReason);

        Assert.Null(detail.Item);
    }

    private static DateOnly Date(int day) => new(2026, 9, day);
}

internal sealed class SnapshotMutationInterceptor : DbCommandInterceptor
{
    private Action? mutation;

    public bool Mutated { get; private set; }

    public List<bool> TransactionalReads { get; } = [];

    public void Start(Action action) => mutation = action;

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        if (mutation is null)
        {
            return base.ReaderExecutedAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }

        if (SessionValidationCommandTestHelper.IsPersistedSessionValidation(command))
        {
            return base.ReaderExecutedAsync(
                command,
                eventData,
                result,
                cancellationToken);
        }

        var transactional = command.Transaction is not null;
        TransactionalReads.Add(transactional);

        if (transactional && !Mutated)
        {
            mutation();
            Mutated = true;
        }

        return base.ReaderExecutedAsync(
            command,
            eventData,
            result,
            cancellationToken);
    }
}