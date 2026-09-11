using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Clients;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Application.SupplierAdvances;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Application.Suppliers;
using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Desktop.ViewModels;
using FinancialManagement.Desktop.Views;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.Tests;

public sealed class Aop05AdvanceWorkflowTests
{
    private static readonly DateOnly PaymentDate = new(2026, 2, 4);
    private static readonly DateTimeOffset FirstAllocationUtc =
        new(2026, 2, 4, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void View_exposes_a_permission_scoped_source_selector_and_keeps_one_cancel_per_dialog()
    {
        WpfTestHost.Invoke(() =>
        {
            var view = Arrange(new PaymentManagementView());
            var route = Assert.IsType<Button>(view.FindName("ShowAdvanceButton"));
            Assert.Equal("Affecter une avance", route.Content);
            AssertBinding(route, Button.CommandProperty, "ShowAdvanceCommand");
            AssertBinding(route, UIElement.VisibilityProperty, "CanApplyAdvancePermission");

            var sourceDialog = Assert.IsType<Border>(view.FindName("AdvanceSourceDialog"));
            var sourceGrid = Assert.IsType<DataGrid>(view.FindName("AdvanceSourceDataGrid"));
            Assert.Equal(
                new[] { "Référence", "Tiers", "Date", "Mode", "Montant", "Déjà affecté", "Disponible" },
                sourceGrid.Columns.Select(column => column.Header));
            Assert.Equal(
                new[]
                {
                    "Reference", "PartyName", "PaymentDate", "PaymentMethod", "Amount",
                    "AllocatedAmount", "AvailableAmount"
                },
                sourceGrid.Columns.Select(ColumnBindingPath));

            var search = Assert.IsType<TextBox>(view.FindName("AdvanceSourceSearchInput"));
            Assert.Equal(
                UpdateSourceTrigger.PropertyChanged,
                Assert.IsType<Binding>(BindingOperations.GetBinding(search, TextBox.TextProperty))
                    .UpdateSourceTrigger);
            var enter = Assert.Single(search.InputBindings.OfType<KeyBinding>());
            Assert.Equal(Key.Enter, enter.Key);
            Assert.Equal(
                "SearchAdvanceSourcesCommand",
                Assert.IsType<Binding>(BindingOperations.GetBinding(
                    enter, InputBinding.CommandProperty)).Path.Path);

            Assert.Single(Buttons(sourceDialog), button => Equals(button.Content, "Annuler"));
            Assert.Single(
                Buttons(Assert.IsType<Border>(view.FindName("PaymentAdvanceDialog"))),
                button => Equals(button.Content, "Annuler"));
        });
    }

    [Fact]
    public async Task Authorized_customer_selects_an_existing_advance_and_refreshes_list_and_history()
    {
        var state = new CustomerWorkflowState();
        using var coordinator = new DashboardOperationCoordinator();
        var paymentService = new CustomerPaymentServiceStub(state);
        var advanceService = new CustomerAdvanceServiceStub(state);
        var viewModel = new CustomerPaymentsViewModel(
            paymentService,
            new CustomerObligationReaderStub(state),
            new CustomerObligationLookupStub(),
            advanceService,
            new UnusedClientLookupService(),
            new AuthenticatedSession(),
            new PermissionAuthorizationService(canModify: true),
            coordinator);

        await viewModel.LoadAsync();
        Assert.True(viewModel.ShowAdvanceCommand.CanExecute(null));

        await Assert.IsType<AsyncRelayCommand>(viewModel.ShowAdvanceCommand).ExecuteAsync(null);

        Assert.True(viewModel.IsAdvanceSourceVisible);
        var source = Assert.Single(viewModel.AdvanceSources);
        Assert.Equal("ENC-ADV-001", source.Reference);
        Assert.Equal(MoneyFormatter.FormatMinor(30_000), source.AvailableAmount);
        Assert.Equal(1, paymentService.ListReadCount);

        viewModel.SelectedAdvanceSource = source;
        await Assert.IsType<AsyncRelayCommand>(viewModel.SelectAdvanceSourceCommand).ExecuteAsync(null);

        Assert.False(viewModel.IsAdvanceSourceVisible);
        Assert.True(viewModel.IsAdvanceVisible);
        Assert.Equal("Client Atlas", viewModel.DetailParty);
        Assert.Equal(MoneyFormatter.FormatMinor(30_000), viewModel.AdvanceAvailable);
        var target = Assert.Single(viewModel.Obligations);
        target.AllocationAmount = "300,00";

        await Assert.IsType<AsyncRelayCommand>(viewModel.ApplyAdvanceCommand).ExecuteAsync(null);

        Assert.NotNull(advanceService.LastCommand);
        Assert.Equal(CustomerWorkflowState.SourcePaymentId, advanceService.LastCommand!.CustomerPaymentId);
        var allocation = Assert.Single(advanceService.LastCommand.Allocations!);
        Assert.Equal(CustomerWorkflowState.TargetSaleId, allocation.SaleId);
        Assert.Equal(30_000, allocation.AmountMinor);
        Assert.Equal(0, paymentService.CreateCount);
        Assert.Equal(100_000, state.PaymentAmountMinor);
        Assert.Equal(2, state.PaymentCount);
        Assert.True(viewModel.IsDetailVisible);
        Assert.False(viewModel.IsAdvanceVisible);
        Assert.Equal(MoneyFormatter.FormatMinor(100_000), viewModel.DetailPaidAmount);
        Assert.Equal(MoneyFormatter.FormatMinor(0), viewModel.DetailRemainingAmount);
        Assert.Equal("Réglé", viewModel.DetailStatus);
        Assert.Equal(2, viewModel.DetailHistory.Count);
        Assert.Equal(2, state.ObligationPageReadCount);
        Assert.Equal(1, state.ObligationDetailReadCount);
    }

    [Fact]
    public async Task Supplier_uses_the_same_reachable_source_selection_workflow()
    {
        var state = new SupplierWorkflowState();
        using var coordinator = new DashboardOperationCoordinator();
        var viewModel = new SupplierPaymentsViewModel(
            new SupplierPaymentServiceStub(),
            new SupplierObligationReaderStub(),
            new SupplierObligationLookupStub(),
            new UnusedSupplierAdvanceService(),
            new UnusedSupplierLookupService(),
            new AuthenticatedSession(),
            new PermissionAuthorizationService(canModify: true),
            coordinator);

        await viewModel.LoadAsync();
        await Assert.IsType<AsyncRelayCommand>(viewModel.ShowAdvanceCommand).ExecuteAsync(null);
        var source = Assert.Single(viewModel.AdvanceSources);
        Assert.Equal("DEC-ADV-001", source.Reference);

        viewModel.SelectedAdvanceSource = source;
        await Assert.IsType<AsyncRelayCommand>(viewModel.SelectAdvanceSourceCommand).ExecuteAsync(null);

        Assert.False(viewModel.IsAdvanceSourceVisible);
        Assert.True(viewModel.IsAdvanceVisible);
        Assert.Equal("Fournisseur Atlas", viewModel.DetailParty);
        Assert.Equal("Rechercher une dette / achat à régler", viewModel.AdvanceTargetSearchLabel);
        Assert.Single(viewModel.Obligations);
    }

    [Fact]
    public async Task User_without_modify_permission_cannot_open_or_execute_the_route()
    {
        var state = new CustomerWorkflowState();
        using var coordinator = new DashboardOperationCoordinator();
        var paymentService = new CustomerPaymentServiceStub(state);
        var viewModel = new CustomerPaymentsViewModel(
            paymentService,
            new CustomerObligationReaderStub(state),
            new CustomerObligationLookupStub(),
            new CustomerAdvanceServiceStub(state),
            new UnusedClientLookupService(),
            new AuthenticatedSession(),
            new PermissionAuthorizationService(canModify: false),
            coordinator);

        await viewModel.LoadAsync();

        Assert.False(viewModel.CanApplyAdvancePermission);
        Assert.False(viewModel.ShowAdvanceCommand.CanExecute(null));
        await Assert.IsType<AsyncRelayCommand>(viewModel.ShowAdvanceCommand).ExecuteAsync(null);
        Assert.False(viewModel.IsAdvanceSourceVisible);
        Assert.Equal(0, paymentService.ListReadCount);
    }

    [Fact]
    public async Task Stale_source_with_no_remaining_advance_is_rejected_before_target_selection()
    {
        var state = new CustomerWorkflowState { SourceIsStale = true };
        using var coordinator = new DashboardOperationCoordinator();
        var viewModel = new CustomerPaymentsViewModel(
            new CustomerPaymentServiceStub(state),
            new CustomerObligationReaderStub(state),
            new CustomerObligationLookupStub(),
            new CustomerAdvanceServiceStub(state),
            new UnusedClientLookupService(),
            new AuthenticatedSession(),
            new PermissionAuthorizationService(canModify: true),
            coordinator);

        await viewModel.LoadAsync();
        await Assert.IsType<AsyncRelayCommand>(viewModel.ShowAdvanceCommand).ExecuteAsync(null);
        viewModel.SelectedAdvanceSource = Assert.Single(viewModel.AdvanceSources);

        await Assert.IsType<AsyncRelayCommand>(viewModel.SelectAdvanceSourceCommand).ExecuteAsync(null);

        Assert.True(viewModel.IsAdvanceSourceVisible);
        Assert.False(viewModel.IsAdvanceVisible);
        Assert.Empty(viewModel.AdvanceSources);
        Assert.Contains("n'est plus disponible", viewModel.WorkflowErrorMessage);
    }

    private static T Arrange<T>(T view)
        where T : FrameworkElement
    {
        view.Measure(new Size(1100, 700));
        view.Arrange(new Rect(0, 0, 1100, 700));
        view.UpdateLayout();
        return view;
    }

    private static void AssertBinding(
        DependencyObject target,
        DependencyProperty property,
        string path) =>
        Assert.Equal(
            path,
            Assert.IsType<Binding>(BindingOperations.GetBinding(target, property)).Path.Path);

    private static string ColumnBindingPath(DataGridColumn column) =>
        Assert.IsType<Binding>(Assert.IsType<DataGridTextColumn>(column).Binding).Path.Path;

    private static Button[] Buttons(DependencyObject parent) =>
        FindVisualChildren<Button>(parent).ToArray();

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent)
        where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
                yield return match;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private sealed class CustomerWorkflowState
    {
        public const long SourcePaymentId = 17;
        public const long TargetSaleId = 29;
        public bool Applied { get; set; }
        public bool SourceIsStale { get; init; }
        public int PaymentCount { get; } = 2;
        public long PaymentAmountMinor { get; } = 100_000;
        public int ObligationPageReadCount { get; set; }
        public int ObligationDetailReadCount { get; set; }
    }

    private sealed class CustomerPaymentServiceStub(CustomerWorkflowState state)
        : ICustomerPaymentService
    {
        public int ListReadCount { get; private set; }
        public int CreateCount { get; private set; }

        public Task<CustomerPaymentListResult> GetPageAsync(
            CustomerPaymentListQuery query,
            CancellationToken cancellationToken = default)
        {
            ListReadCount++;
            var items = new[]
            {
                new CustomerPaymentListItem(
                    16, "ENC-FULL-001", PaymentDate, 7, "Client Atlas", 20_000,
                    PaymentMethod.Cash, 20_000, 0, null),
                new CustomerPaymentListItem(
                    CustomerWorkflowState.SourcePaymentId, "ENC-ADV-001", PaymentDate, 7,
                    "Client Atlas", state.PaymentAmountMinor, PaymentMethod.BankTransfer,
                    70_000, 30_000, "Avance existante")
            };
            return Task.FromResult(CustomerPaymentListResult.Succeeded(query, items, items.Length));
        }

        public Task<CustomerPaymentDetailResult> GetByIdAsync(
            long customerPaymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(CustomerPaymentDetailResult.Succeeded(new CustomerPaymentDetailItem(
                CustomerWorkflowState.SourcePaymentId,
                "ENC-ADV-001",
                PaymentDate,
                7,
                "Client Atlas",
                state.PaymentAmountMinor,
                PaymentMethod.BankTransfer,
                state.SourceIsStale ? state.PaymentAmountMinor : 70_000,
                state.SourceIsStale ? 0 : 30_000,
                "Avance existante",
                [
                    new CustomerPaymentAllocationDetailItem(
                        1,
                        CustomerPaymentAllocationTargetType.Sale,
                        CustomerWorkflowState.TargetSaleId,
                        "VTE-001",
                        100_000,
                        70_000)
                ])));

        public Task<CreateCustomerPaymentResult> CreateAsync(
            CreateCustomerPaymentCommand command,
            CancellationToken cancellationToken = default)
        {
            CreateCount++;
            throw new NotSupportedException();
        }
    }

    private sealed class CustomerObligationReaderStub(CustomerWorkflowState state)
        : ICustomerPaymentObligationReadService
    {
        public Task<CustomerPaymentObligationPageResult> GetPageAsync(
            CustomerPaymentObligationPageQuery query,
            CancellationToken cancellationToken = default)
        {
            state.ObligationPageReadCount++;
            var paid = state.Applied ? 100_000 : 70_000;
            var remaining = state.Applied ? 0 : 30_000;
            var status = state.Applied
                ? FinancialManagement.Application.Payments.PaymentObligationStatus.Settled
                : FinancialManagement.Application.Payments.PaymentObligationStatus.PartiallyPaid;
            var item = new CustomerPaymentObligationListItem(
                CustomerPaymentObligationTargetType.Sale,
                CustomerWorkflowState.TargetSaleId,
                "VTE-001",
                PaymentDate,
                7,
                "Client Atlas",
                100_000,
                paid,
                remaining,
                status);
            return Task.FromResult(CustomerPaymentObligationPageResult.Succeeded(query, [item], 1));
        }

        public Task<CustomerPaymentObligationDetailResult> GetDetailAsync(
            CustomerPaymentObligationTargetType targetType,
            long targetId,
            CancellationToken cancellationToken = default)
        {
            state.ObligationDetailReadCount++;
            return Task.FromResult(CustomerPaymentObligationDetailResult.Succeeded(
                new CustomerPaymentObligationDetailItem(
                    CustomerPaymentObligationTargetType.Sale,
                    CustomerWorkflowState.TargetSaleId,
                    "VTE-001",
                    PaymentDate,
                    7,
                    "Client Atlas",
                    100_000,
                    100_000,
                    0,
                    FinancialManagement.Application.Payments.PaymentObligationStatus.Settled,
                    [
                        new CustomerPaymentObligationHistoryItem(
                            201,
                            CustomerWorkflowState.SourcePaymentId,
                            "ENC-ADV-001",
                            PaymentDate,
                            FirstAllocationUtc,
                            PaymentMethod.BankTransfer,
                            70_000,
                            70_000,
                            30_000),
                        new CustomerPaymentObligationHistoryItem(
                            202,
                            CustomerWorkflowState.SourcePaymentId,
                            "ENC-ADV-001",
                            PaymentDate,
                            FirstAllocationUtc.AddHours(1),
                            PaymentMethod.BankTransfer,
                            30_000,
                            100_000,
                            0)
                    ])));
        }
    }

    private sealed class CustomerObligationLookupStub : ICustomerPaymentObligationLookupService
    {
        public Task<CustomerPaymentObligationLookupResult> SearchAsync(
            CustomerPaymentObligationLookupQuery query,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(7, query.ClientId);
            Assert.Equal(CustomerPaymentObligationLookupPurpose.ApplyAdvance, query.Purpose);
            return Task.FromResult(CustomerPaymentObligationLookupResult.Succeeded(
                query,
                [
                    new CustomerPaymentObligationLookupItem(
                        CustomerPaymentObligationTargetType.Sale,
                        CustomerWorkflowState.TargetSaleId,
                        "VTE-001",
                        PaymentDate,
                        PaymentDate.AddDays(30),
                        100_000,
                        30_000)
                ]));
        }
    }

    private sealed class CustomerAdvanceServiceStub(CustomerWorkflowState state)
        : ICustomerAdvanceService
    {
        public ApplyCustomerAdvanceCommand? LastCommand { get; private set; }

        public Task<ApplyCustomerAdvanceResult> ApplyAsync(
            ApplyCustomerAdvanceCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            state.Applied = true;
            return Task.FromResult(ApplyCustomerAdvanceResult.Succeeded(
                command.CustomerPaymentId,
                "ENC-ADV-001",
                30_000,
                30_000,
                0,
                1));
        }
    }

    private sealed class SupplierWorkflowState
    {
        public const long SourcePaymentId = 31;
        public const long TargetPurchaseId = 41;
    }

    private sealed class SupplierPaymentServiceStub : ISupplierPaymentService
    {
        public Task<SupplierPaymentListResult> GetPageAsync(
            SupplierPaymentListQuery query,
            CancellationToken cancellationToken = default)
        {
            var item = new SupplierPaymentListItem(
                SupplierWorkflowState.SourcePaymentId,
                "DEC-ADV-001",
                PaymentDate,
                9,
                "Fournisseur Atlas",
                100_000,
                PaymentMethod.Cash,
                70_000,
                30_000,
                null);
            return Task.FromResult(SupplierPaymentListResult.Succeeded(query, [item], 1));
        }

        public Task<SupplierPaymentDetailResult> GetByIdAsync(
            long supplierPaymentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SupplierPaymentDetailResult.Succeeded(new SupplierPaymentDetailItem(
                SupplierWorkflowState.SourcePaymentId,
                "DEC-ADV-001",
                PaymentDate,
                9,
                "Fournisseur Atlas",
                100_000,
                PaymentMethod.Cash,
                70_000,
                30_000,
                null,
                [])));

        public Task<CreateSupplierPaymentResult> CreateAsync(
            CreateSupplierPaymentCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SupplierObligationReaderStub : ISupplierPaymentObligationReadService
    {
        public Task<SupplierPaymentObligationPageResult> GetPageAsync(
            SupplierPaymentObligationPageQuery query,
            CancellationToken cancellationToken = default)
        {
            var item = new SupplierPaymentObligationListItem(
                SupplierPaymentObligationTargetType.Purchase,
                SupplierWorkflowState.TargetPurchaseId,
                "ACH-001",
                PaymentDate,
                9,
                "Fournisseur Atlas",
                100_000,
                70_000,
                30_000,
                FinancialManagement.Application.Payments.PaymentObligationStatus.PartiallyPaid);
            return Task.FromResult(SupplierPaymentObligationPageResult.Succeeded(query, [item], 1));
        }

        public Task<SupplierPaymentObligationDetailResult> GetDetailAsync(
            SupplierPaymentObligationTargetType targetType,
            long targetId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class SupplierObligationLookupStub : ISupplierPaymentObligationLookupService
    {
        public Task<SupplierPaymentObligationLookupResult> SearchAsync(
            SupplierPaymentObligationLookupQuery query,
            CancellationToken cancellationToken = default)
        {
            Assert.Equal(9, query.SupplierId);
            Assert.Equal(SupplierPaymentObligationLookupPurpose.ApplyAdvance, query.Purpose);
            return Task.FromResult(SupplierPaymentObligationLookupResult.Succeeded(
                query,
                [
                    new SupplierPaymentObligationLookupItem(
                        SupplierPaymentObligationTargetType.Purchase,
                        SupplierWorkflowState.TargetPurchaseId,
                        "ACH-001",
                        PaymentDate,
                        PaymentDate.AddDays(30),
                        100_000,
                        30_000)
                ]));
        }
    }

    private sealed class AuthenticatedSession : ICurrentUserSession
    {
        public bool IsAuthenticated => true;
        public long? UserId => 42;
        public string? FullName => "AOP-05 Test";
        public string? Username => "aop05-test";
        public UserRole? Role => UserRole.Responsable;
    }

    private sealed class PermissionAuthorizationService(bool canModify) : IAuthorizationService
    {
        public Task<bool> CanAsync(
            PermissionModule module,
            PermissionAction action,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(action != PermissionAction.Modify || canModify);

        public Task<EffectiveModulePermission> GetEffectivePermissionsAsync(
            PermissionModule module,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new EffectiveModulePermission(true, true, canModify));

        public bool CanAccessResponsibleCapability(ResponsibleCapability capability) => true;
    }

    private sealed class UnusedClientLookupService : IClientLookupService
    {
        public Task<ClientLookupResult> SearchAsync(
            ClientLookupQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedSupplierLookupService : ISupplierLookupService
    {
        public Task<SupplierLookupResult> SearchAsync(
            SupplierLookupQuery query,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnusedSupplierAdvanceService : ISupplierAdvanceService
    {
        public Task<ApplySupplierAdvanceResult> ApplyAsync(
            ApplySupplierAdvanceCommand command,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
