using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.InitialCustomerReceivables;
using FinancialManagement.Application.Sales;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.InitialCustomerReceivables;

internal sealed class InitialCustomerReceivableService(
    FinancialManagementDbContext dbContext,
    ICurrentUserSession currentUserSession,
    IAuthorizationService authorizationService,
    IBusinessReferenceGenerator referenceGenerator,
    TimeProvider timeProvider) : IInitialCustomerReceivableService
{
    public async Task<CreateInitialCustomerReceivableResult> CreateAsync(
        CreateInitialCustomerReceivableCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();

        if (!currentUserSession.IsAuthenticated || currentUserSession.UserId is not long userId)
        {
            return CreateInitialCustomerReceivableResult.Failed(
                CreateInitialCustomerReceivableFailureReason.NotAuthenticated,
                command.InitialAmountMinor);
        }

        if (!authorizationService.CanAccessResponsibleCapability(ResponsibleCapability.Administration))
        {
            return CreateInitialCustomerReceivableResult.Failed(
                CreateInitialCustomerReceivableFailureReason.Forbidden,
                command.InitialAmountMinor);
        }

        if (command.ClientId <= 0)
        {
            return CreateInitialCustomerReceivableResult.Failed(
                CreateInitialCustomerReceivableFailureReason.InvalidClient,
                command.InitialAmountMinor);
        }

        if (command.InitialAmountMinor <= 0)
        {
            return CreateInitialCustomerReceivableResult.Failed(
                CreateInitialCustomerReceivableFailureReason.InvalidAmount,
                command.InitialAmountMinor);
        }

        var clientExists = await dbContext.Clients
            .AsNoTracking()
            .AnyAsync(client => client.Id == command.ClientId, cancellationToken);

        if (!clientExists)
        {
            return CreateInitialCustomerReceivableResult.Failed(
                CreateInitialCustomerReceivableFailureReason.InvalidClient,
                command.InitialAmountMinor);
        }

        var createdAtUtc = timeProvider.GetUtcNow();
        var receivable = new InitialCustomerReceivable
        {
            Reference = referenceGenerator.GenerateInitialCustomerReceivableReference(
                command.ReceivableDate),
            ClientId = command.ClientId,
            ReceivableDate = command.ReceivableDate,
            InitialAmountMinor = command.InitialAmountMinor,
            DueDate = command.DueDate,
            Observation = command.Observation,
            CreatedByUserId = userId,
            CreatedAtUtc = createdAtUtc
        };

        dbContext.InitialCustomerReceivables.Add(receivable);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateInitialCustomerReceivableResult.Succeeded(
            receivable.Id,
            receivable.Reference,
            receivable.InitialAmountMinor);
    }
}
