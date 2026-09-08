using FinancialManagement.Application.Backup;
using FinancialManagement.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Tests;

public sealed partial class DatabaseBackupServiceTests
{
    public static IEnumerable<object[]> InvalidAllocationBackups()
    {
        foreach (var customer in new[] { true, false })
        foreach (var initial in new[] { true, false })
        foreach (var corruption in new[] { "payment", "target", "overflow" })
            yield return [customer, initial, corruption];
    }

    [Theory]
    [MemberData(nameof(InvalidAllocationBackups))]
    public async Task Restore_rejects_impossible_allocation_aggregates_before_replacing_live_database(
        bool customer, bool initial, string corruption)
    {
        await using var source = await BackupFixture.CreateAsync(authenticate: true);
        await using var destination = await BackupFixture.CreateAsync(authenticate: true);
        await source.AddRepresentativeDataAsync("Corrupt allocations");
        destination.Context.Clients.Add(NewClient("Live marker"));
        await destination.Context.SaveChangesAsync();
        var amount = corruption == "overflow" ? long.MaxValue : 200;
        var paymentAmount = corruption == "payment" ? 100 : long.MaxValue;
        var targetAmount = corruption == "target" ? 100 : long.MaxValue;

        if (customer)
        {
            var payment = await source.Context.CustomerPayments.SingleAsync();
            payment.AmountMinor = paymentAmount;
            var sale = await source.Context.Sales.SingleAsync();
            sale.TotalAmountMinor = targetAmount;
            var receivable = new InitialCustomerReceivable
            {
                Reference = "Initial", ClientId = payment.ClientId!.Value,
                InitialAmountMinor = targetAmount, CreatedByUserId = payment.CreatedByUserId
            };
            source.Context.Add(receivable);
            await source.Context.SaveChangesAsync();
            foreach (var allocationAmount in corruption == "overflow" ? new[] { amount, 1 } : new[] { amount })
                source.Context.Add(new CustomerPaymentAllocation
                {
                    CustomerPaymentId = payment.Id, SaleId = initial ? null : sale.Id,
                    InitialCustomerReceivableId = initial ? receivable.Id : null,
                    AmountMinor = allocationAmount
                });
        }
        else
        {
            var payment = await source.Context.SupplierPayments.SingleAsync();
            payment.AmountMinor = paymentAmount;
            var purchase = await source.Context.Purchases.SingleAsync();
            purchase.TotalAmountMinor = targetAmount;
            var payable = new InitialSupplierPayable
            {
                Reference = "Initial", SupplierId = payment.SupplierId,
                InitialAmountMinor = targetAmount, CreatedByUserId = payment.CreatedByUserId
            };
            source.Context.Add(payable);
            await source.Context.SaveChangesAsync();
            foreach (var allocationAmount in corruption == "overflow" ? new[] { amount, 1 } : new[] { amount })
                source.Context.Add(new SupplierPaymentAllocation
                {
                    SupplierPaymentId = payment.Id, PurchaseId = initial ? null : purchase.Id,
                    InitialSupplierPayableId = initial ? payable.Id : null,
                    AmountMinor = allocationAmount
                });
        }
        await source.Context.SaveChangesAsync();
        // Backups remain available for preserving historical data; only importing
        // financially impossible state into the live database is rejected.
        var backup = await source.Service.CreateLocalBackupAsync();
        Assert.True(backup.Success);
        var artifactBefore = await HashFileAsync(backup.BackupPath!);
        var liveBefore = await HashFileAsync(destination.LivePath);
        var swapped = false;
        destination.RestoreTestHook.AfterAtomicReplace = _ => swapped = true;

        var result = await destination.Service.RestoreAsync(new(backup.BackupPath!));

        Assert.False(result.Success);
        Assert.Equal(DatabaseBackupFailureReason.InvalidBackup, result.FailureReason);
        Assert.Null(result.PreRestoreBackupPath);
        Assert.False(swapped);
        Assert.True(destination.Session.IsAuthenticated);
        Assert.Equal(artifactBefore, await HashFileAsync(backup.BackupPath!));
        Assert.Equal(liveBefore, await HashFileAsync(destination.LivePath));
        await destination.AssertLiveMarkerAsync();
    }
}
