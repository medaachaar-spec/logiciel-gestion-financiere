using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Tests;

public sealed class CustomerPaymentReceiptServiceTests
{
    [Fact]
    public async Task Single_obligation_partial_receipt_uses_payment_reference_and_historical_amounts()
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser("receipt-partial", UserRole.Responsable);
        var client = database.AddClient("Al Amal Market");
        var sale = database.AddSale(user, "VTE-4300", 430_000, client.Id);
        var payment = database.AddCustomerPayment(
            user, "ENC-PARTIAL-001", new DateOnly(2026, 9, 11), 100_000, client.Id);
        database.AddCustomerAllocation(payment, 100_000, saleId: sale.Id);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerReceiptService.GetAsync(payment.Id);

        Assert.True(result.Success);
        var receipt = Assert.IsType<CustomerPaymentReceiptItem>(result.Item);
        Assert.Equal("ENC-PARTIAL-001", receipt.PaymentReference);
        Assert.Equal("Al Amal Market", receipt.CustomerName);
        Assert.Equal(100_000, receipt.PaymentAmountMinor);
        Assert.Equal(PaymentMethod.Cash, receipt.PaymentMethod);
        var allocation = Assert.Single(receipt.Allocations);
        Assert.Equal(CustomerPaymentAllocationTargetType.Sale, allocation.TargetType);
        Assert.Equal("VTE-4300", allocation.TargetReference);
        Assert.Equal((430_000L, 100_000L, 100_000L, 330_000L),
            (allocation.OriginalObligationAmountMinor,
                allocation.AllocatedAmountMinor,
                allocation.CumulativePaidAfterAllocationMinor,
                allocation.RemainingAfterAllocationMinor));
    }

    [Fact]
    public async Task Historical_receipt_preserves_state_after_selected_payment_when_later_payments_exist()
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser("receipt-history", UserRole.Responsable);
        var client = database.AddClient("Client historique");
        var sale = database.AddSale(user, "VTE-HISTORY-RECEIPT", 300_000, client.Id);
        var amounts = new long[] { 50_000, 100_000, 120_000, 30_000 };
        var payments = new List<FinancialManagement.Domain.Entities.CustomerPayment>();
        for (var index = 0; index < amounts.Length; index++)
        {
            var payment = database.AddCustomerPayment(
                user,
                $"ENC-H-{index + 1}",
                new DateOnly(2026, 9, 11),
                amounts[index],
                client.Id,
                createdAtUtc: new DateTimeOffset(2026, 9, 11, 8, index, 0, TimeSpan.Zero));
            database.AddCustomerAllocation(payment, amounts[index], saleId: sale.Id);
            payments.Add(payment);
        }
        await database.AuthenticateAsync(user);

        var result = await database.CustomerReceiptService.GetAsync(payments[1].Id);

        var allocation = Assert.Single(result.Item!.Allocations);
        Assert.Equal(100_000, allocation.AllocatedAmountMinor);
        Assert.Equal(150_000, allocation.CumulativePaidAfterAllocationMinor);
        Assert.Equal(150_000, allocation.RemainingAfterAllocationMinor);
    }

    [Fact]
    public async Task Fully_settled_obligation_receipt_has_zero_remaining()
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser("receipt-settled", UserRole.Responsable);
        var client = database.AddClient("Client soldé");
        var sale = database.AddSale(user, "VTE-SETTLED-RECEIPT", 75_000, client.Id);
        var payment = database.AddCustomerPayment(
            user, "ENC-SETTLED", new DateOnly(2026, 9, 11), 75_000, client.Id);
        database.AddCustomerAllocation(payment, 75_000, saleId: sale.Id);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerReceiptService.GetAsync(payment.Id);

        var allocation = Assert.Single(result.Item!.Allocations);
        Assert.Equal(75_000, allocation.CumulativePaidAfterAllocationMinor);
        Assert.Equal(0, allocation.RemainingAfterAllocationMinor);
    }

    [Fact]
    public async Task Multi_obligation_receipt_keeps_payment_total_and_shows_unapplied_advance()
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser("receipt-multi", UserRole.Responsable);
        var client = database.AddClient("Client multi");
        var sale = database.AddSale(user, "VTE-MULTI", 90_000, client.Id);
        var receivable = database.AddReceivable(user, client, "CRI-MULTI", 80_000);
        var payment = database.AddCustomerPayment(
            user, "ENC-MULTI", new DateOnly(2026, 9, 11), 100_000, client.Id,
            PaymentMethod.BankTransfer);
        database.AddCustomerAllocation(payment, 40_000, saleId: sale.Id);
        database.AddCustomerAllocation(payment, 30_000, receivableId: receivable.Id);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerReceiptService.GetAsync(payment.Id);

        var receipt = result.Item!;
        Assert.Equal(100_000, receipt.PaymentAmountMinor);
        Assert.Equal(30_000, receipt.UnallocatedAmountMinor);
        Assert.Equal(2, receipt.Allocations.Count);
        Assert.Contains(receipt.Allocations, item =>
            item.TargetType == CustomerPaymentAllocationTargetType.Sale
            && item.AllocatedAmountMinor == 40_000);
        Assert.Contains(receipt.Allocations, item =>
            item.TargetType == CustomerPaymentAllocationTargetType.InitialCustomerReceivable
            && item.AllocatedAmountMinor == 30_000);
    }

    [Fact]
    public async Task Repeated_payment_target_allocations_use_each_authoritative_history_position()
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser("receipt-repeated", UserRole.Responsable);
        var client = database.AddClient("Client répétition");
        var sale = database.AddSale(user, "VTE-REPEATED", 100_000, client.Id);
        var payment = database.AddCustomerPayment(
            user, "ENC-REPEATED", new DateOnly(2026, 9, 11), 50_000, client.Id);
        database.AddCustomerAllocation(payment, 30_000, saleId: sale.Id);
        database.AddCustomerAllocation(payment, 20_000, saleId: sale.Id);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerReceiptService.GetAsync(payment.Id);

        Assert.Equal(new long[] { 30_000, 50_000 },
            result.Item!.Allocations.Select(item => item.CumulativePaidAfterAllocationMinor));
        Assert.Equal(new long[] { 70_000, 50_000 },
            result.Item.Allocations.Select(item => item.RemainingAfterAllocationMinor));
    }

    [Fact]
    public async Task Reprint_is_read_only_and_does_not_create_payment_or_change_audit_timestamp()
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser("receipt-readonly", UserRole.Responsable);
        var client = database.AddClient("Client lecture seule");
        var sale = database.AddSale(user, "VTE-READONLY", 100_000, client.Id);
        var payment = database.AddCustomerPayment(
            user, "ENC-READONLY", new DateOnly(2026, 9, 11), 40_000, client.Id);
        database.AddCustomerAllocation(payment, 40_000, saleId: sale.Id);
        await database.AuthenticateAsync(user);
        var originalTimestamp = payment.CreatedAtUtc;
        var paymentCount = await database.Context.CustomerPayments.CountAsync();
        var allocationCount = await database.Context.CustomerPaymentAllocations.CountAsync();
        database.Context.ChangeTracker.Clear();

        var first = await database.CustomerReceiptService.GetAsync(payment.Id);
        var second = await database.CustomerReceiptService.GetAsync(payment.Id);

        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.Equal(paymentCount, await database.Context.CustomerPayments.CountAsync());
        Assert.Equal(allocationCount, await database.Context.CustomerPaymentAllocations.CountAsync());
        Assert.Equal(originalTimestamp,
            await database.Context.CustomerPayments.AsNoTracking()
                .Where(item => item.Id == payment.Id)
                .Select(item => item.CreatedAtUtc)
                .SingleAsync());
        Assert.Empty(database.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Inconsistent_allocation_history_fails_closed()
    {
        using var database = new PaymentReadTestDatabase();
        var user = database.AddUser("receipt-corrupt", UserRole.Responsable);
        var client = database.AddClient("Client incohérent");
        var sale = database.AddSale(user, "VTE-CORRUPT-RECEIPT", 100_000, client.Id);
        var payment = database.AddCustomerPayment(
            user, "ENC-CORRUPT-RECEIPT", new DateOnly(2026, 9, 11), 120_000, client.Id);
        database.AddCustomerAllocation(payment, 120_000, saleId: sale.Id);
        await database.AuthenticateAsync(user);

        var result = await database.CustomerReceiptService.GetAsync(payment.Id);

        Assert.False(result.Success);
        Assert.Equal(CustomerPaymentReceiptFailureReason.DataIntegrityViolation, result.FailureReason);
        Assert.Null(result.Item);
    }
}
