using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Initialization;
using FinancialManagement.Domain.Entities;
using FinancialManagement.Domain.Enums;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialManagement.Infrastructure.Initialization;

internal sealed class ApplicationInitializationService(
    FinancialManagementDbContext dbContext,
    IPasswordHasher passwordHasher,
    TimeProvider timeProvider) : IApplicationInitializationService
{
    public async Task<ApplicationInitializationState> GetStateAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // A single consistent snapshot for the three flags, including during bootstrap.
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var hasUsers = await dbContext.Users.AnyAsync(cancellationToken);
        var hasResponsible = await dbContext.Users.AnyAsync(
            user => user.IsActive && user.Role == UserRole.Responsable, cancellationToken);
        var hasOpening = await dbContext.TreasuryOpeningBalances.AnyAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(!hasUsers, hasResponsible, hasOpening);
    }

    public async Task<BootstrapResponsibleResult> BootstrapResponsibleAsync(
        BootstrapResponsibleCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        cancellationToken.ThrowIfCancellationRequested();
        User? createdUser = null;
        var connectionOpened = false;
        try
        {
            await dbContext.Database.OpenConnectionAsync(cancellationToken);
            connectionOpened = true;
            var connection = (SqliteConnection)dbContext.Database.GetDbConnection();
            // BEGIN IMMEDIATE reserves the only SQLite writer BEFORE testing the zero-user condition.
            // This protects independent DbContexts, providers and processes, not just one application instance.
            await using var transaction = connection.BeginTransaction(deferred: false);
            await using var enlistment = await dbContext.Database.UseTransactionAsync(transaction, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (await dbContext.Users.AnyAsync(cancellationToken))
            {
                return BootstrapResponsibleResult.Failed(BootstrapResponsibleFailureReason.BootstrapUnavailable);
            }

            if (string.IsNullOrWhiteSpace(command.FullName))
                return BootstrapResponsibleResult.Failed(BootstrapResponsibleFailureReason.InvalidFullName);
            if (string.IsNullOrWhiteSpace(command.Username))
                return BootstrapResponsibleResult.Failed(BootstrapResponsibleFailureReason.InvalidUsername);
            if (string.IsNullOrEmpty(command.Password))
                return BootstrapResponsibleResult.Failed(BootstrapResponsibleFailureReason.InvalidPassword);

            var now = timeProvider.GetUtcNow();
            createdUser = new User
            {
                FullName = command.FullName.Trim(),
                // Preserve the existing authentication/SQLite username comparison semantics.
                Username = command.Username,
                PasswordHash = passwordHasher.HashPassword(command.Password),
                Role = UserRole.Responsable,
                IsActive = true,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            };
            cancellationToken.ThrowIfCancellationRequested();
            dbContext.Users.Add(createdUser);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return BootstrapResponsibleResult.Succeeded(createdUser.Id);
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BootstrapResponsibleResult.Failed(BootstrapResponsibleFailureReason.ConcurrentOperation);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 5 or 6 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            return BootstrapResponsibleResult.Failed(BootstrapResponsibleFailureReason.ConcurrentOperation);
        }
        finally
        {
            // Failed or cancelled writes must not leak into a later SaveChanges on the scoped context.
            if (createdUser is not null)
                dbContext.Entry(createdUser).State = EntityState.Detached;
            if (connectionOpened)
                await dbContext.Database.CloseConnectionAsync();
        }
    }
}
