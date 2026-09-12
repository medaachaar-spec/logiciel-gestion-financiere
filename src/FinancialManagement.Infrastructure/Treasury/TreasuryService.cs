using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Sales;
using FinancialManagement.Application.Treasury;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Treasury;

internal sealed class TreasuryService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    IBusinessReferenceGenerator referenceGenerator,
    TimeProvider timeProvider) : ITreasuryService
{
    public async Task<CreateTreasuryOpeningBalanceResult> CreateOpeningBalanceAsync(
        CreateTreasuryOpeningBalanceCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is not long userId)
        {
            return CreateTreasuryOpeningBalanceResult.Failed(
                CreateTreasuryOpeningBalanceFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Treasury,
                cancellationToken))
        {
            return CreateTreasuryOpeningBalanceResult.Failed(
                CreateTreasuryOpeningBalanceFailureReason.Forbidden);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Treasury,
                cancellationToken)
            || currentUserSession.UserId != userId)
        {
            return CreateTreasuryOpeningBalanceResult.Failed(
                CreateTreasuryOpeningBalanceFailureReason.Forbidden);
        }

        if (await dbContext.TreasuryOpeningBalances.AnyAsync(cancellationToken))
        {
            return CreateTreasuryOpeningBalanceResult.Failed(
                CreateTreasuryOpeningBalanceFailureReason.AlreadyInitialized);
        }

        var openingBalance = new TreasuryOpeningBalance
        {
            OpeningDate = command.OpeningDate,
            CashAmountMinor = command.CashAmountMinor,
            BankAmountMinor = command.BankAmountMinor,
            CreatedByUserId = userId,
            CreatedAtUtc = timeProvider.GetUtcNow()
        };

        dbContext.TreasuryOpeningBalances.Add(openingBalance);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException sqlite
            && sqlite.SqliteExtendedErrorCode == 2067
            && sqlite.Message.Contains("UX_TreasuryOpeningBalance_Singleton", StringComparison.Ordinal))
        {
            // Another initialization won after AnyAsync. Keep its values and discard only our failed insert.
            dbContext.Entry(openingBalance).State = EntityState.Detached;
            return CreateTreasuryOpeningBalanceResult.Failed(
                CreateTreasuryOpeningBalanceFailureReason.AlreadyInitialized);
        }

        return CreateTreasuryOpeningBalanceResult.Succeeded(
            openingBalance.Id,
            openingBalance.OpeningDate,
            openingBalance.CashAmountMinor,
            openingBalance.BankAmountMinor);
    }

    public async Task<CreateTreasuryTransferResult> CreateTransferAsync(
        CreateTreasuryTransferCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is not long userId)
        {
            return CreateTreasuryTransferResult.Failed(
                CreateTreasuryTransferFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Treasury,
                cancellationToken))
        {
            return CreateTreasuryTransferResult.Failed(
                CreateTreasuryTransferFailureReason.Forbidden);
        }

        if (command.AmountMinor <= 0)
        {
            return CreateTreasuryTransferResult.Failed(
                CreateTreasuryTransferFailureReason.InvalidAmount);
        }

        if (!Enum.IsDefined(command.FromAccount) || !Enum.IsDefined(command.ToAccount))
        {
            return CreateTreasuryTransferResult.Failed(
                CreateTreasuryTransferFailureReason.InvalidAccount);
        }

        if (command.FromAccount == command.ToAccount)
        {
            return CreateTreasuryTransferResult.Failed(
                CreateTreasuryTransferFailureReason.SameAccount);
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Treasury,
                cancellationToken)
            || currentUserSession.UserId != userId)
        {
            return CreateTreasuryTransferResult.Failed(
                CreateTreasuryTransferFailureReason.Forbidden);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var localDateTime = TimeZoneInfo.ConvertTime(createdAtUtc, timeProvider.LocalTimeZone);
        var transferDate = DateOnly.FromDateTime(localDateTime.DateTime);
        var transfer = new TreasuryTransfer
        {
            Reference = referenceGenerator.GenerateTreasuryTransferReference(transferDate),
            TransferDate = transferDate,
            FromAccount = command.FromAccount,
            ToAccount = command.ToAccount,
            AmountMinor = command.AmountMinor,
            Observation = command.Observation,
            CreatedByUserId = userId,
            CreatedAtUtc = createdAtUtc
        };

        dbContext.TreasuryTransfers.Add(transfer);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateTreasuryTransferResult.Succeeded(
            transfer.Id,
            transfer.Reference,
            transfer.TransferDate,
            transfer.FromAccount,
            transfer.ToAccount,
            transfer.AmountMinor);
    }

    public async Task<TreasurySummaryResult> GetSummaryAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return TreasurySummaryResult.Failed(TreasurySummaryFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Treasury,
                cancellationToken))
        {
            return TreasurySummaryResult.Failed(TreasurySummaryFailureReason.Forbidden);
        }

        // A composed Dashboard read owns its transaction; only manage our standalone one.
        await using var transaction = dbContext.Database.CurrentTransaction is null
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (!await authorizationService.CanAccessResponsibleCapabilityAsync(
                ResponsibleCapability.Treasury,
                cancellationToken))
        {
            return TreasurySummaryResult.Failed(TreasurySummaryFailureReason.Forbidden);
        }

        await EnsurePersistedEnumsAreValidAsync(cancellationToken);

        var opening = await dbContext.TreasuryOpeningBalances
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        var customerCashInflowsMinor = await SumCustomerPaymentsAsync(
            PaymentMethod.Cash,
            cancellationToken);
        var customerBankInflowsMinor = await SumCustomerPaymentsAsync(
            PaymentMethod.BankTransfer,
            cancellationToken);
        var supplierCashOutflowsMinor = await SumSupplierPaymentsAsync(
            PaymentMethod.Cash,
            cancellationToken);
        var supplierBankOutflowsMinor = await SumSupplierPaymentsAsync(
            PaymentMethod.BankTransfer,
            cancellationToken);
        var expenseCashOutflowsMinor = await SumExpensesAsync(
            PaymentMethod.Cash,
            cancellationToken);
        var expenseBankOutflowsMinor = await SumExpensesAsync(
            PaymentMethod.BankTransfer,
            cancellationToken);
        var cashToBankTransfersMinor = await SumTransfersAsync(
            TreasuryAccount.Cash,
            TreasuryAccount.Bank,
            cancellationToken);
        var bankToCashTransfersMinor = await SumTransfersAsync(
            TreasuryAccount.Bank,
            TreasuryAccount.Cash,
            cancellationToken);

        var cashOpeningMinor = opening?.CashAmountMinor ?? 0;
        var bankOpeningMinor = opening?.BankAmountMinor ?? 0;
        long cashBalanceMinor;
        long bankBalanceMinor;
        long totalTreasuryMinor;
        checked
        {
            cashBalanceMinor = cashOpeningMinor
                + customerCashInflowsMinor
                - supplierCashOutflowsMinor
                - expenseCashOutflowsMinor
                - cashToBankTransfersMinor
                + bankToCashTransfersMinor;
            bankBalanceMinor = bankOpeningMinor
                + customerBankInflowsMinor
                - supplierBankOutflowsMinor
                - expenseBankOutflowsMinor
                + cashToBankTransfersMinor
                - bankToCashTransfersMinor;
            totalTreasuryMinor = cashBalanceMinor + bankBalanceMinor;
        }

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return TreasurySummaryResult.Succeeded(
            opening is not null,
            opening?.OpeningDate,
            cashOpeningMinor,
            bankOpeningMinor,
            customerCashInflowsMinor,
            customerBankInflowsMinor,
            supplierCashOutflowsMinor,
            supplierBankOutflowsMinor,
            expenseCashOutflowsMinor,
            expenseBankOutflowsMinor,
            cashToBankTransfersMinor,
            bankToCashTransfersMinor,
            cashBalanceMinor,
            bankBalanceMinor,
            totalTreasuryMinor);
    }

    private async Task EnsurePersistedEnumsAreValidAsync(CancellationToken cancellationToken)
    {
        var invalidPaymentMethodExists = await dbContext.CustomerPayments
                .AnyAsync(
                    payment => payment.PaymentMethod != PaymentMethod.Cash
                        && payment.PaymentMethod != PaymentMethod.BankTransfer,
                    cancellationToken)
            || await dbContext.SupplierPayments.AnyAsync(
                payment => payment.PaymentMethod != PaymentMethod.Cash
                    && payment.PaymentMethod != PaymentMethod.BankTransfer,
                cancellationToken)
            || await dbContext.Expenses.AnyAsync(
                expense => expense.PaymentMethod != PaymentMethod.Cash
                    && expense.PaymentMethod != PaymentMethod.BankTransfer,
                cancellationToken);

        if (invalidPaymentMethodExists)
        {
            throw new InvalidOperationException(
                "An invalid payment method is persisted in the treasury data.");
        }

        var invalidTransferAccountExists = await dbContext.TreasuryTransfers.AnyAsync(
            transfer =>
                (transfer.FromAccount != TreasuryAccount.Cash
                    && transfer.FromAccount != TreasuryAccount.Bank)
                || (transfer.ToAccount != TreasuryAccount.Cash
                    && transfer.ToAccount != TreasuryAccount.Bank),
            cancellationToken);

        if (invalidTransferAccountExists)
        {
            throw new InvalidOperationException(
                "An invalid treasury account is persisted in the transfer data.");
        }
    }

    private Task<long> SumCustomerPaymentsAsync(
        PaymentMethod method,
        CancellationToken cancellationToken) =>
        dbContext.CustomerPayments
            .Where(payment => payment.PaymentMethod == method)
            .SumAsync(payment => payment.AmountMinor, cancellationToken);

    private Task<long> SumSupplierPaymentsAsync(
        PaymentMethod method,
        CancellationToken cancellationToken) =>
        dbContext.SupplierPayments
            .Where(payment => payment.PaymentMethod == method)
            .SumAsync(payment => payment.AmountMinor, cancellationToken);

    private Task<long> SumExpensesAsync(
        PaymentMethod method,
        CancellationToken cancellationToken) =>
        dbContext.Expenses
            .Where(expense => expense.PaymentMethod == method)
            .SumAsync(expense => expense.AmountMinor, cancellationToken);

    private Task<long> SumTransfersAsync(
        TreasuryAccount fromAccount,
        TreasuryAccount toAccount,
        CancellationToken cancellationToken) =>
        dbContext.TreasuryTransfers
            .Where(transfer => transfer.FromAccount == fromAccount
                && transfer.ToAccount == toAccount)
            .SumAsync(transfer => transfer.AmountMinor, cancellationToken);
}
