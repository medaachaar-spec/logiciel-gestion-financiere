using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.InitialSupplierPayables;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.InitialSupplierPayables;

internal sealed class InitialSupplierPayableService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    IBusinessReferenceGenerator referenceGenerator,
    TimeProvider timeProvider) : IInitialSupplierPayableService
{
    public async Task<CreateInitialSupplierPayableResult> CreateAsync(
        CreateInitialSupplierPayableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is not long userId)
        {
            return CreateInitialSupplierPayableResult.Failed(
                CreateInitialSupplierPayableFailureReason.NotAuthenticated,
                command.InitialAmountMinor);
        }

        if (!authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.Administration))
        {
            return CreateInitialSupplierPayableResult.Failed(
                CreateInitialSupplierPayableFailureReason.Forbidden,
                command.InitialAmountMinor);
        }

        if (command.SupplierId <= 0)
        {
            return CreateInitialSupplierPayableResult.Failed(
                CreateInitialSupplierPayableFailureReason.InvalidSupplier,
                command.InitialAmountMinor);
        }

        if (command.InitialAmountMinor <= 0)
        {
            return CreateInitialSupplierPayableResult.Failed(
                CreateInitialSupplierPayableFailureReason.InvalidAmount,
                command.InitialAmountMinor);
        }

        var supplierExists = await dbContext.Suppliers
            .AsNoTracking()
            .AnyAsync(supplier => supplier.Id == command.SupplierId, cancellationToken);

        if (!supplierExists)
        {
            return CreateInitialSupplierPayableResult.Failed(
                CreateInitialSupplierPayableFailureReason.InvalidSupplier,
                command.InitialAmountMinor);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var payable = new InitialSupplierPayable
        {
            Reference = referenceGenerator.GenerateInitialSupplierPayableReference(
                command.PayableDate),
            SupplierId = command.SupplierId,
            PayableDate = command.PayableDate,
            InitialAmountMinor = command.InitialAmountMinor,
            DueDate = command.DueDate,
            Observation = command.Observation,
            CreatedByUserId = userId,
            CreatedAtUtc = createdAtUtc
        };

        dbContext.InitialSupplierPayables.Add(payable);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateInitialSupplierPayableResult.Succeeded(
            payable.Id,
            payable.Reference,
            payable.InitialAmountMinor);
    }
}
