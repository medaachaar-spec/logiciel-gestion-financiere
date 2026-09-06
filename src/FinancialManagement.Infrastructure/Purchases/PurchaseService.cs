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
    public async Task<PurchaseListResult> GetPageAsync(
        PurchaseListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return PurchaseListResult.Failed(query, PurchaseListFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Purchases,
                PermissionAction.View,
                cancellationToken))
        {
            return PurchaseListResult.Failed(query, PurchaseListFailureReason.Forbidden);
        }

        if (query.PageNumber < 1)
        {
            return PurchaseListResult.Failed(query, PurchaseListFailureReason.InvalidPageNumber);
        }

        if (query.PageSize is < 1 or > 200)
        {
            return PurchaseListResult.Failed(query, PurchaseListFailureReason.InvalidPageSize);
        }

        if (query.FromDate is DateOnly fromDate
            && query.ToDate is DateOnly toDate
            && fromDate > toDate)
        {
            return PurchaseListResult.Failed(query, PurchaseListFailureReason.InvalidDateRange);
        }

        var skip = ((long)query.PageNumber - 1) * query.PageSize;
        if (skip > int.MaxValue)
        {
            return PurchaseListResult.Failed(query, PurchaseListFailureReason.InvalidPageNumber);
        }

        var purchases =
            from purchase in dbContext.Purchases.AsNoTracking()
            join supplier in dbContext.Suppliers.AsNoTracking()
                on purchase.SupplierId equals supplier.Id
            select new
            {
                purchase.Id,
                purchase.Reference,
                purchase.PurchaseDate,
                purchase.TotalAmountMinor,
                purchase.SupplierId,
                SupplierName = supplier.Name,
                purchase.DueDate,
                purchase.Observation
            };

        if (query.FromDate is DateOnly inclusiveFromDate)
        {
            purchases = purchases.Where(purchase => purchase.PurchaseDate >= inclusiveFromDate);
        }

        if (query.ToDate is DateOnly inclusiveToDate)
        {
            purchases = purchases.Where(purchase => purchase.PurchaseDate <= inclusiveToDate);
        }

        var searchText = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(searchText))
        {
            purchases = purchases.Where(purchase =>
                purchase.Reference.Contains(searchText)
                || purchase.SupplierName.Contains(searchText));
        }

        var totalCount = await purchases.CountAsync(cancellationToken);
        var items = await purchases
            .OrderByDescending(purchase => purchase.PurchaseDate)
            .ThenByDescending(purchase => purchase.Id)
            .Skip((int)skip)
            .Take(query.PageSize)
            .Select(purchase => new PurchaseListItem(
                purchase.Id,
                purchase.Reference,
                purchase.PurchaseDate,
                purchase.TotalAmountMinor,
                purchase.SupplierId,
                purchase.SupplierName,
                purchase.DueDate,
                purchase.Observation))
            .ToListAsync(cancellationToken);

        return PurchaseListResult.Succeeded(query, items, totalCount);
    }

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
