using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Sales;

internal sealed class SaleService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    IBusinessReferenceGenerator referenceGenerator,
    TimeProvider timeProvider) : ISaleService
{
    public async Task<SaleListResult> GetPageAsync(
        SaleListQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated)
        {
            return SaleListResult.Failed(query, SaleListFailureReason.NotAuthenticated);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Sales,
                PermissionAction.View,
                cancellationToken))
        {
            return SaleListResult.Failed(query, SaleListFailureReason.Forbidden);
        }

        if (query.PageNumber < 1)
        {
            return SaleListResult.Failed(query, SaleListFailureReason.InvalidPageNumber);
        }

        if (query.PageSize is < 1 or > 200)
        {
            return SaleListResult.Failed(query, SaleListFailureReason.InvalidPageSize);
        }

        if (query.FromDate is DateOnly fromDate
            && query.ToDate is DateOnly toDate
            && fromDate > toDate)
        {
            return SaleListResult.Failed(query, SaleListFailureReason.InvalidDateRange);
        }

        var skip = ((long)query.PageNumber - 1) * query.PageSize;
        if (skip > int.MaxValue)
        {
            return SaleListResult.Failed(query, SaleListFailureReason.InvalidPageNumber);
        }

        var sales =
            from sale in dbContext.Sales.AsNoTracking()
            join client in dbContext.Clients.AsNoTracking()
                on sale.ClientId equals client.Id into clients
            from client in clients.DefaultIfEmpty()
            select new
            {
                sale.Id,
                sale.Reference,
                sale.SaleDate,
                sale.TotalAmountMinor,
                sale.ClientId,
                ClientName = client == null ? null : client.Name,
                sale.DueDate,
                sale.Observation
            };

        if (query.FromDate is DateOnly inclusiveFromDate)
        {
            sales = sales.Where(sale => sale.SaleDate >= inclusiveFromDate);
        }

        if (query.ToDate is DateOnly inclusiveToDate)
        {
            sales = sales.Where(sale => sale.SaleDate <= inclusiveToDate);
        }

        var searchText = query.SearchText?.Trim();
        if (!string.IsNullOrEmpty(searchText))
        {
            sales = sales.Where(sale =>
                sale.Reference.Contains(searchText)
                || (sale.ClientName != null && sale.ClientName.Contains(searchText)));
        }

        var totalCount = await sales.CountAsync(cancellationToken);
        var items = await sales
            .OrderByDescending(sale => sale.SaleDate)
            .ThenByDescending(sale => sale.Id)
            .Skip((int)skip)
            .Take(query.PageSize)
            .Select(sale => new SaleListItem(
                sale.Id,
                sale.Reference,
                sale.SaleDate,
                sale.TotalAmountMinor,
                sale.ClientId,
                sale.ClientName,
                sale.DueDate,
                sale.Observation))
            .ToListAsync(cancellationToken);

        return SaleListResult.Succeeded(query, items, totalCount);
    }

    public async Task<CreateSaleResult> CreateAsync(
        CreateSaleCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is not long userId)
        {
            return CreateSaleResult.Failed(
                CreateSaleFailureReason.NotAuthenticated,
                command.TotalAmountMinor);
        }

        if (!await authorizationService.CanAsync(
                PermissionModule.Sales,
                PermissionAction.Create,
                cancellationToken))
        {
            return CreateSaleResult.Failed(
                CreateSaleFailureReason.NotAuthorized,
                command.TotalAmountMinor);
        }

        if (command.TotalAmountMinor <= 0)
        {
            return CreateSaleResult.Failed(
                CreateSaleFailureReason.InvalidTotalAmount,
                command.TotalAmountMinor);
        }

        if (command.InitialPayment is { } initialPayment)
        {
            if (initialPayment.AmountMinor <= 0)
            {
                return CreateSaleResult.Failed(
                    CreateSaleFailureReason.InvalidInitialPaymentAmount,
                    command.TotalAmountMinor);
            }

            if (!Enum.IsDefined(initialPayment.PaymentMethod))
            {
                return CreateSaleResult.Failed(
                    CreateSaleFailureReason.InvalidPaymentMethod,
                    command.TotalAmountMinor);
            }
        }

        if (command.ClientId is long clientId)
        {
            var clientExists = await dbContext.Clients
                .AsNoTracking()
                .AnyAsync(client => client.Id == clientId, cancellationToken);

            if (!clientExists)
            {
                return CreateSaleResult.Failed(
                    CreateSaleFailureReason.ClientNotFound,
                    command.TotalAmountMinor);
            }
        }
        else if (command.InitialPayment is null)
        {
            return CreateSaleResult.Failed(
                CreateSaleFailureReason.AnonymousSaleRequiresInitialPayment,
                command.TotalAmountMinor);
        }
        else if (command.InitialPayment.AmountMinor != command.TotalAmountMinor)
        {
            return CreateSaleResult.Failed(
                CreateSaleFailureReason.AnonymousSalePaymentMustMatchTotal,
                command.TotalAmountMinor);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var localDateTime = TimeZoneInfo.ConvertTime(createdAtUtc, timeProvider.LocalTimeZone);
        var businessDate = DateOnly.FromDateTime(localDateTime.DateTime);
        var saleReference = referenceGenerator.GenerateSaleReference(businessDate);
        var paymentReference = command.InitialPayment is null
            ? null
            : referenceGenerator.GenerateCustomerPaymentReference(businessDate);
        var allocatedAmountMinor = command.InitialPayment is null
            ? 0
            : Math.Min(command.TotalAmountMinor, command.InitialPayment.AmountMinor);
        var remainingReceivableMinor = command.TotalAmountMinor - allocatedAmountMinor;
        var unallocatedAdvanceMinor = command.InitialPayment is null
            ? 0
            : command.InitialPayment.AmountMinor - allocatedAmountMinor;

        await using var transaction = await dbContext.Database
            .BeginTransactionAsync(cancellationToken);

        if (!await authorizationService.CanAsync(
                PermissionModule.Sales,
                PermissionAction.Create,
                cancellationToken)
            || currentUserSession.UserId != userId)
        {
            return CreateSaleResult.Failed(
                CreateSaleFailureReason.NotAuthorized,
                command.TotalAmountMinor);
        }

        var sale = new Sale
        {
            Reference = saleReference,
            SaleDate = businessDate,
            TotalAmountMinor = command.TotalAmountMinor,
            ClientId = command.ClientId,
            Observation = command.Observation,
            DueDate = command.DueDate,
            CreatedByUserId = userId,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };
        dbContext.Sales.Add(sale);

        CustomerPayment? payment = null;
        if (command.InitialPayment is not null)
        {
            payment = new CustomerPayment
            {
                Reference = paymentReference!,
                ClientId = command.ClientId,
                PaymentDate = businessDate,
                AmountMinor = command.InitialPayment.AmountMinor,
                PaymentMethod = command.InitialPayment.PaymentMethod,
                Observation = command.InitialPayment.Observation,
                CreatedByUserId = userId,
                CreatedAtUtc = createdAtUtc
            };
            dbContext.CustomerPayments.Add(payment);
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (payment is not null)
        {
            dbContext.CustomerPaymentAllocations.Add(new CustomerPaymentAllocation
            {
                CustomerPaymentId = payment.Id,
                SaleId = sale.Id,
                InitialCustomerReceivableId = null,
                AmountMinor = allocatedAmountMinor,
                CreatedAtUtc = createdAtUtc
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        return CreateSaleResult.Succeeded(
            sale.Id,
            sale.Reference,
            payment?.Id,
            payment?.Reference,
            command.TotalAmountMinor,
            allocatedAmountMinor,
            remainingReceivableMinor,
            unallocatedAdvanceMinor);
    }
}
