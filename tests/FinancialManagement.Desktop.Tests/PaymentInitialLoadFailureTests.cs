using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Clients;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Desktop.ViewModels;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.Tests;

public sealed class PaymentInitialLoadFailureTests
{
    [Fact]
    public async Task Initial_load_and_manual_refresh_expose_the_same_integrity_error()
    {
        var reader = new FailingCustomerObligationReader();
        using var coordinator = new DashboardOperationCoordinator();
        var viewModel = new CustomerPaymentsViewModel(
            new UnusedCustomerPaymentService(),
            reader,
            new UnusedCustomerObligationLookupService(),
            new UnusedCustomerAdvanceService(),
            new UnusedClientLookupService(),
            new AuthenticatedSession(),
            new AllowingAuthorizationService(),
            coordinator);

        await viewModel.LoadAsync();

        Assert.Equal("Les données d'affectation sont incohérentes.", viewModel.ListErrorMessage);
        Assert.True(viewModel.HasListError);
        Assert.False(viewModel.ShowEmptyState);
        Assert.Empty(viewModel.Payments);
        Assert.Equal(1, reader.PageReadCount);

        await viewModel.RefreshAsync();

        Assert.Equal("Les données d'affectation sont incohérentes.", viewModel.ListErrorMessage);
        Assert.True(viewModel.HasListError);
        Assert.False(viewModel.ShowEmptyState);
        Assert.Empty(viewModel.Payments);
        Assert.Equal(2, reader.PageReadCount);
    }

    private sealed class FailingCustomerObligationReader : ICustomerPaymentObligationReadService
    {
        public int PageReadCount { get; private set; }

        public Task<CustomerPaymentObligationPageResult> GetPageAsync(
            CustomerPaymentObligationPageQuery query,
            CancellationToken cancellationToken = default)
        {
            PageReadCount++;
            return Task.FromResult(CustomerPaymentObligationPageResult.Failed(
                query,
                CustomerPaymentObligationReadFailureReason.DataIntegrityViolation));
        }

        public Task<CustomerPaymentObligationDetailResult> GetDetailAsync(
            CustomerPaymentObligationTargetType targetType,
            long targetId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class AuthenticatedSession : ICurrentUserSession
    {
        public bool IsAuthenticated => true;
        public long? UserId => 42;
        public string? FullName => "Payment VM Test";
        public string? Username => "payment-vm-test";
        public UserRole? Role => UserRole.Responsable;
    }

    private sealed class AllowingAuthorizationService : IAuthorizationService
    {
        public Task<bool> CanAsync(
            PermissionModule module,
            PermissionAction action,
            CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<EffectiveModulePermission> GetEffectivePermissionsAsync(
            PermissionModule module,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EffectiveModulePermission(true, true, true));

        public bool CanAccessResponsibleCapability(ResponsibleCapability capability) => true;
    }

    private sealed class UnusedCustomerPaymentService : ICustomerPaymentService
    {
        public Task<CreateCustomerPaymentResult> CreateAsync(
            CreateCustomerPaymentCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CustomerPaymentListResult> GetPageAsync(
            CustomerPaymentListQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<CustomerPaymentDetailResult> GetByIdAsync(
            long customerPaymentId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedCustomerObligationLookupService : ICustomerPaymentObligationLookupService
    {
        public Task<CustomerPaymentObligationLookupResult> SearchAsync(
            CustomerPaymentObligationLookupQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedCustomerAdvanceService : ICustomerAdvanceService
    {
        public Task<ApplyCustomerAdvanceResult> ApplyAsync(
            ApplyCustomerAdvanceCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedClientLookupService : IClientLookupService
    {
        public Task<ClientLookupResult> SearchAsync(
            ClientLookupQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
