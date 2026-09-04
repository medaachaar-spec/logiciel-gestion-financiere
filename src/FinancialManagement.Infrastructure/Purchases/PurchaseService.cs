using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Purchases;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Purchases;

internal sealed class PurchaseService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    IBusinessReferenceGenerator referenceGenerator,
    TimeProvider timeProvider) : IPurchaseService
{
    public async Task<CreatePurchaseResult> CreateAsync(
        CreatePurchaseCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is not long userId)
        {
            return CreatePurchaseResult.Failed(
                CreatePurchaseFailureReason.NotAuthenticated,
                command.TotalAmountMinor);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Purchases,
                PermissionAction.Create,
                cancellationToken))
        {
            return CreatePurchaseResult.Failed(
                CreatePurchaseFailureReason.NotAuthorized,
                command.TotalAmountMinor);
        }

        if (command.TotalAmountMinor <= 0)
        {
            return CreatePurchaseResult.Failed(
                CreatePurchaseFailureReason.InvalidTotalAmount,
                command.TotalAmountMinor);
        }

        if (command.InitialPayment is { } initialPayment)
        {
            if (initialPayment.AmountMinor <= 0)
            {
                return CreatePurchaseResult.Failed(
                    CreatePurchaseFailureReason.InvalidInitialPaymentAmount,
                    command.TotalAmountMinor);
            }

            if (!Enum.IsDefined(initialPayment.PaymentMethod))
            {
                return CreatePurchaseResult.Failed(
                    CreatePurchaseFailureReason.InvalidPaymentMethod,
                    command.TotalAmountMinor);
            }
        }

        var supplierExists = await dbContext.Suppliers
            .AsNoTracking()
            .AnyAsync(supplier => supplier.Id == command.SupplierId, cancellationToken);

        if (!supplierExists)
        {
            return CreatePurchaseResult.Failed(
                CreatePurchaseFailureReason.SupplierNotFound,
                command.TotalAmountMinor);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var localDateTime = TimeZoneInfo.ConvertTime(createdAtUtc, timeProvider.LocalTimeZone);
        var businessDate = DateOnly.FromDateTime(localDateTime.DateTime);
        var purchaseReference = referenceGenerator.GeneratePurchaseReference(businessDate);
        var paymentReference = command.InitialPayment is null
            ? null
            : referenceGenerator.GenerateSupplierPaymentReference(businessDate);
        var allocatedAmountMinor = command.InitialPayment is null
            ? 0
            : Math.Min(command.TotalAmountMinor, command.InitialPayment.AmountMinor);
        var remainingPayableMinor = command.TotalAmountMinor - allocatedAmountMinor;
        var unallocatedSupplierAdvanceMinor = command.InitialPayment is null
            ? 0
            : command.InitialPayment.AmountMinor - allocatedAmountMinor;

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        var purchase = new Purchase
        {
            Reference = purchaseReference,
            PurchaseDate = businessDate,
            TotalAmountMinor = command.TotalAmountMinor,
            SupplierId = command.SupplierId,
            Observation = command.Observation,
            DueDate = command.DueDate,
            CreatedByUserId = userId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
        dbContext.Purchases.Add(purchase);

        SupplierPayment? payment = null;
        if (command.InitialPayment is not null)
        {
            payment = new SupplierPayment
            {
                Reference = paymentReference!,
                SupplierId = command.SupplierId,
                PaymentDate = businessDate,
                AmountMinor = command.InitialPayment.AmountMinor,
                PaymentMethod = command.InitialPayment.PaymentMethod,
                Observation = command.InitialPayment.Observation,
                CreatedByUserId = userId,
                CreatedAtUtc = createdAtUtc
            };
            dbContext.SupplierPayments.Add(payment);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (payment is not null)
        {
            dbContext.SupplierPaymentAllocations.Add(new SupplierPaymentAllocation
            {
                SupplierPaymentId = payment.Id,
                PurchaseId = purchase.Id,
                InitialSupplierPayableId = null,
                AmountMinor = allocatedAmountMinor,
                CreatedAtUtc = createdAtUtc
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return CreatePurchaseResult.Succeeded(
            purchase.Id,
            purchase.Reference,
            payment?.Id,
            payment?.Reference,
            command.TotalAmountMinor,
            allocatedAmountMinor,
            remainingPayableMinor,
            unallocatedSupplierAdvanceMinor);
    }
}
