using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Clients;
using FinancialManagement.Application.CustomerAdvances;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class CustomerPaymentsViewModel : PaymentManagementViewModelBase
{
    private readonly ICustomerPaymentService _paymentService;
    private readonly ICustomerPaymentObligationReadService _obligationReadService;
    private readonly ICustomerPaymentObligationLookupService _obligationLookupService;
    private readonly ICustomerAdvanceService _advanceService;
    private readonly IClientLookupService _clientLookupService;
    private readonly ICustomerPaymentReceiptDialogService? _receiptDialogService;

    public CustomerPaymentsViewModel(
        ICustomerPaymentService paymentService,
        ICustomerPaymentObligationReadService obligationReadService,
        ICustomerPaymentObligationLookupService obligationLookupService,
        ICustomerAdvanceService advanceService,
        IClientLookupService clientLookupService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator,
        ICustomerPaymentReceiptDialogService? receiptDialogService = null)
        : base(
            currentUserSession,
            authorizationService,
            operationCoordinator,
            PermissionModule.CustomerPayments,
            PermissionModule.Clients,
            "Encaissements clients",
            "Suivez chaque créance, son règlement et l'historique de ses encaissements.",
            "client",
            "Client",
            "encaissements")
    {
        ArgumentNullException.ThrowIfNull(paymentService);
        ArgumentNullException.ThrowIfNull(obligationReadService);
        ArgumentNullException.ThrowIfNull(obligationLookupService);
        ArgumentNullException.ThrowIfNull(advanceService);
        ArgumentNullException.ThrowIfNull(clientLookupService);

        _paymentService = paymentService;
        _obligationReadService = obligationReadService;
        _obligationLookupService = obligationLookupService;
        _advanceService = advanceService;
        _clientLookupService = clientLookupService;
        _receiptDialogService = receiptDialogService;
    }

    protected override string SingularPaymentLower => "encaissement";

    public override bool SupportsCustomerReceipts => _receiptDialogService is not null;

    protected override Task<CustomerPaymentReceiptDialogResult> OpenCustomerReceiptAsync(
        long paymentId,
        CancellationToken cancellationToken) =>
        _receiptDialogService?.ShowAsync(paymentId, cancellationToken)
        ?? Task.FromResult(CustomerPaymentReceiptDialogResult.Failed(
            "Le service d'aperçu du reçu n'est pas disponible."));

    internal override async Task<PaymentPageData> GetPageAsync(
        PaymentListQueryData query,
        CancellationToken cancellationToken)
    {
        var applicationQuery = new CustomerPaymentObligationPageQuery(
            query.PageNumber,
            query.PageSize,
            query.FromDate,
            query.ToDate,
            query.SearchText,
            query.Status);
        var result = await _obligationReadService.GetPageAsync(applicationQuery, cancellationToken);
        if (!result.Success)
        {
            var failure = MapObligationReadFailure(result.FailureReason);
            return new PaymentPageData(
                false,
                failure.Kind,
                failure.Message,
                [],
                result.PageNumber,
                result.PageSize,
                0,
                0);
        }

        return new PaymentPageData(
            true,
            PaymentFailureKind.None,
            null,
            result.Items.Select(item => new PaymentObligationListData(
                MapTarget(item.TargetType),
                item.TargetId,
                item.Reference,
                item.BusinessDate,
                item.ClientId,
                item.ClientName,
                item.OriginalAmountMinor,
                item.PaidAmountMinor,
                item.RemainingAmountMinor,
                item.Status)).ToArray(),
            result.PageNumber,
            result.PageSize,
            result.TotalCount,
            result.TotalPages);
    }

    internal override async Task<PaymentObligationDetailResponse> GetObligationDetailAsync(
        PaymentTargetKind targetKind,
        long targetId,
        CancellationToken cancellationToken)
    {
        var result = await _obligationReadService.GetDetailAsync(
            MapReadTarget(targetKind), targetId, cancellationToken);
        if (!result.Success || result.Item is null)
        {
            var failure = MapObligationReadFailure(result.FailureReason);
            return new PaymentObligationDetailResponse(false, failure.Kind, failure.Message, null);
        }

        var item = result.Item;
        return new PaymentObligationDetailResponse(
            true,
            PaymentFailureKind.None,
            null,
            new PaymentObligationDetailData(
                MapTarget(item.TargetType),
                item.TargetId,
                item.Reference,
                item.BusinessDate,
                item.ClientId,
                item.ClientName,
                item.OriginalAmountMinor,
                item.PaidAmountMinor,
                item.RemainingAmountMinor,
                item.Status,
                item.History.Select(history => new PaymentHistoryData(
                    history.PaymentId,
                    history.PaymentReference,
                    history.PaymentDate,
                    history.PaymentCreatedAtUtc,
                    history.PaymentMethod,
                    history.AllocatedAmountMinor,
                    history.CumulativePaidAmountMinor,
                    history.RemainingAfterPaymentMinor)).ToArray()));
    }

    internal override async Task<PaymentDetailResponse> GetDetailAsync(
        long paymentId,
        CancellationToken cancellationToken)
    {
        var result = await _paymentService.GetByIdAsync(paymentId, cancellationToken);
        if (!result.Success || result.Item is null)
        {
            var failure = MapDetailFailure(result.FailureReason);
            return new PaymentDetailResponse(false, failure.Kind, failure.Message, null);
        }

        var item = result.Item;
        return new PaymentDetailResponse(
            true,
            PaymentFailureKind.None,
            null,
            new PaymentDetailData(
                item.Id,
                item.Reference,
                item.PaymentDate,
                item.ClientId,
                item.ClientName,
                item.AmountMinor,
                item.PaymentMethod,
                item.AllocatedAmountMinor,
                item.UnallocatedAmountMinor,
                item.Observation,
                item.Allocations.Select(allocation => new PaymentAllocationDetailData(
                    MapTarget(allocation.TargetType),
                    allocation.TargetId,
                    allocation.TargetReference,
                    allocation.OriginalObligationAmountMinor,
                    allocation.AllocatedAmountMinor)).ToArray()));
    }

    internal override async Task<AdvancePaymentSourceResponse> GetAdvanceSourcesAsync(
        string? searchText,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var sources = new List<AdvancePaymentSourceData>();
        var pageNumber = 1;
        while (true)
        {
            var result = await _paymentService.GetPageAsync(
                new CustomerPaymentListQuery(
                    pageNumber,
                    pageSize,
                    SearchText: searchText),
                cancellationToken);
            if (!result.Success)
            {
                var failure = MapListFailure(result.FailureReason);
                return new AdvancePaymentSourceResponse(false, failure.Kind, failure.Message, []);
            }

            sources.AddRange(result.Items
                .Where(item => item.ClientId is > 0 && item.UnallocatedAmountMinor > 0)
                .Select(item => new AdvancePaymentSourceData(
                    item.Id,
                    item.Reference,
                    item.PaymentDate,
                    item.ClientId!.Value,
                    item.ClientName!,
                    item.AmountMinor,
                    item.PaymentMethod,
                    item.AllocatedAmountMinor,
                    item.UnallocatedAmountMinor)));

            if (pageNumber >= result.TotalPages)
            {
                break;
            }

            pageNumber++;
        }

        return new AdvancePaymentSourceResponse(
            true,
            PaymentFailureKind.None,
            null,
            sources);
    }

    internal override async Task<PartyLookupResponse> SearchPartiesAsync(
        string? searchText,
        int limit,
        CancellationToken cancellationToken)
    {
        var result = await _clientLookupService.SearchAsync(
            new ClientLookupQuery(searchText, limit),
            cancellationToken);
        if (!result.Success)
        {
            var failure = result.FailureReason switch
            {
                ClientLookupFailureReason.NotAuthenticated =>
                    new Failure(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
                ClientLookupFailureReason.Forbidden =>
                    new Failure(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à consulter les clients."),
                _ => new Failure(PaymentFailureKind.Validation, "La recherche de clients n'est pas valide.")
            };
            return new PartyLookupResponse(false, failure.Kind, failure.Message, []);
        }

        return new PartyLookupResponse(
            true,
            PaymentFailureKind.None,
            null,
            result.Items.Select(item => new PartyLookupData(
                item.ClientId,
                item.DisplayName,
                item.Identifier)).ToArray());
    }

    internal override async Task<PaymentObligationResponse> SearchObligationsAsync(
        long partyId,
        bool applyAdvance,
        string? searchText,
        int limit,
        CancellationToken cancellationToken)
    {
        var query = new CustomerPaymentObligationLookupQuery(
            partyId,
            applyAdvance
                ? CustomerPaymentObligationLookupPurpose.ApplyAdvance
                : CustomerPaymentObligationLookupPurpose.CreatePayment,
            searchText,
            limit);
        var result = await _obligationLookupService.SearchAsync(query, cancellationToken);
        if (!result.Success)
        {
            var failure = MapObligationFailure(result.FailureReason);
            return new PaymentObligationResponse(false, failure.Kind, failure.Message, []);
        }

        return new PaymentObligationResponse(
            true,
            PaymentFailureKind.None,
            null,
            result.Items.Select(item => new PaymentObligationData(
                MapTarget(item.TargetType),
                item.TargetId,
                item.Reference,
                item.BusinessDate,
                item.DueDate,
                item.OriginalAmountMinor,
                item.OutstandingAmountMinor)).ToArray());
    }

    internal override async Task<PaymentCreateResponse> CreatePaymentAsync(
        long partyId,
        long amountMinor,
        PaymentMethod paymentMethod,
        string? observation,
        IReadOnlyList<PaymentAllocationRequest> allocations,
        CancellationToken cancellationToken)
    {
        var command = new CreateCustomerPaymentCommand(
            partyId,
            amountMinor,
            paymentMethod,
            observation,
            allocations.Select(MapCreateAllocation).ToArray());
        var result = await _paymentService.CreateAsync(command, cancellationToken);
        if (!result.Success)
        {
            var failure = MapCreateFailure(result.FailureReason);
            return new PaymentCreateResponse(false, failure.Kind, failure.Message, null, null);
        }

        return new PaymentCreateResponse(
            true,
            PaymentFailureKind.None,
            null,
            result.CustomerPaymentId,
            result.CustomerPaymentReference);
    }

    internal override async Task<AdvanceApplyResponse> ApplyAdvanceAsync(
        long paymentId,
        IReadOnlyList<PaymentAllocationRequest> allocations,
        CancellationToken cancellationToken)
    {
        var command = new ApplyCustomerAdvanceCommand(
            paymentId,
            allocations.Select(MapAdvanceAllocation).ToArray());
        var result = await _advanceService.ApplyAsync(command, cancellationToken);
        if (!result.Success)
        {
            var failure = MapAdvanceFailure(result.FailureReason);
            return new AdvanceApplyResponse(false, failure.Kind, failure.Message);
        }

        return new AdvanceApplyResponse(true, PaymentFailureKind.None, null);
    }

    private static CustomerPaymentAllocationInput MapCreateAllocation(PaymentAllocationRequest allocation) =>
        allocation.TargetKind switch
        {
            PaymentTargetKind.Sale => new CustomerPaymentAllocationInput(
                allocation.TargetId,
                null,
                allocation.AmountMinor),
            PaymentTargetKind.InitialCustomerReceivable => new CustomerPaymentAllocationInput(
                null,
                allocation.TargetId,
                allocation.AmountMinor),
            _ => throw new InvalidOperationException("Unsupported customer allocation target.")
        };

    private static CustomerAdvanceAllocationInput MapAdvanceAllocation(PaymentAllocationRequest allocation) =>
        allocation.TargetKind switch
        {
            PaymentTargetKind.Sale => new CustomerAdvanceAllocationInput(
                allocation.TargetId,
                null,
                allocation.AmountMinor),
            PaymentTargetKind.InitialCustomerReceivable => new CustomerAdvanceAllocationInput(
                null,
                allocation.TargetId,
                allocation.AmountMinor),
            _ => throw new InvalidOperationException("Unsupported customer advance target.")
        };

    private static PaymentTargetKind MapTarget(CustomerPaymentAllocationTargetType targetType) => targetType switch
    {
        CustomerPaymentAllocationTargetType.Sale => PaymentTargetKind.Sale,
        CustomerPaymentAllocationTargetType.InitialCustomerReceivable =>
            PaymentTargetKind.InitialCustomerReceivable,
        _ => throw new InvalidOperationException("Unsupported customer payment target.")
    };

    private static PaymentTargetKind MapTarget(CustomerPaymentObligationTargetType targetType) => targetType switch
    {
        CustomerPaymentObligationTargetType.Sale => PaymentTargetKind.Sale,
        CustomerPaymentObligationTargetType.InitialCustomerReceivable =>
            PaymentTargetKind.InitialCustomerReceivable,
        _ => throw new InvalidOperationException("Unsupported customer obligation target.")
    };

    private static CustomerPaymentObligationTargetType MapReadTarget(PaymentTargetKind targetKind) =>
        targetKind switch
        {
            PaymentTargetKind.Sale => CustomerPaymentObligationTargetType.Sale,
            PaymentTargetKind.InitialCustomerReceivable =>
                CustomerPaymentObligationTargetType.InitialCustomerReceivable,
            _ => throw new InvalidOperationException("Unsupported customer obligation target.")
        };

    private static Failure MapObligationReadFailure(CustomerPaymentObligationReadFailureReason reason) =>
        reason switch
        {
            CustomerPaymentObligationReadFailureReason.NotAuthenticated =>
                new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
            CustomerPaymentObligationReadFailureReason.Forbidden =>
                new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à consulter les encaissements clients."),
            CustomerPaymentObligationReadFailureReason.InvalidPageNumber
                or CustomerPaymentObligationReadFailureReason.InvalidPageSize
                or CustomerPaymentObligationReadFailureReason.InvalidDateRange
                or CustomerPaymentObligationReadFailureReason.InvalidStatus
                or CustomerPaymentObligationReadFailureReason.InvalidTarget =>
                new(PaymentFailureKind.Validation, "Les filtres ou l'obligation demandée ne sont pas valides."),
            CustomerPaymentObligationReadFailureReason.NotFound =>
                new(PaymentFailureKind.NotFound, "Cette obligation n'existe plus."),
            _ => new(PaymentFailureKind.DataIntegrity, "Les données d'affectation sont incohérentes.")
        };

    private static Failure MapListFailure(CustomerPaymentListFailureReason reason) => reason switch
    {
        CustomerPaymentListFailureReason.NotAuthenticated =>
            new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
        CustomerPaymentListFailureReason.Forbidden =>
            new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à consulter les encaissements clients."),
        CustomerPaymentListFailureReason.InvalidDateRange =>
            new(PaymentFailureKind.Validation, "La période sélectionnée n'est pas valide."),
        CustomerPaymentListFailureReason.InvalidPaymentMethod =>
            new(PaymentFailureKind.Validation, "Le mode de paiement sélectionné n'est pas valide."),
        CustomerPaymentListFailureReason.InvalidPageNumber or CustomerPaymentListFailureReason.InvalidPageSize =>
            new(PaymentFailureKind.Validation, "La page demandée n'est pas valide."),
        _ => new(PaymentFailureKind.DataIntegrity, "Les données de paiement sont incohérentes. Actualisez avant de réessayer.")
    };

    private static Failure MapDetailFailure(CustomerPaymentDetailFailureReason reason) => reason switch
    {
        CustomerPaymentDetailFailureReason.NotAuthenticated =>
            new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
        CustomerPaymentDetailFailureReason.Forbidden =>
            new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à consulter ce paiement."),
        CustomerPaymentDetailFailureReason.NotFound =>
            new(PaymentFailureKind.NotFound, "Ce paiement n'existe plus. Actualisez la liste."),
        CustomerPaymentDetailFailureReason.InvalidPaymentId =>
            new(PaymentFailureKind.Validation, "Le paiement sélectionné n'est pas valide."),
        _ => new(PaymentFailureKind.DataIntegrity, "Le détail du paiement est incohérent. Actualisez avant de réessayer.")
    };

    private static Failure MapObligationFailure(CustomerPaymentObligationLookupFailureReason reason) => reason switch
    {
        CustomerPaymentObligationLookupFailureReason.NotAuthenticated =>
            new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
        CustomerPaymentObligationLookupFailureReason.Forbidden =>
            new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à consulter les obligations de ce client."),
        CustomerPaymentObligationLookupFailureReason.ClientNotFound =>
            new(PaymentFailureKind.NotFound, "Le client sélectionné n'est plus disponible."),
        CustomerPaymentObligationLookupFailureReason.DataIntegrityViolation =>
            new(PaymentFailureKind.DataIntegrity, "Les obligations ont changé ou sont incohérentes. Actualisez avant de réessayer."),
        _ => new(PaymentFailureKind.Validation, "La recherche d'obligations n'est pas valide.")
    };

    private static Failure MapCreateFailure(CreateCustomerPaymentFailureReason reason) => reason switch
    {
        CreateCustomerPaymentFailureReason.NotAuthenticated =>
            new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
        CreateCustomerPaymentFailureReason.Forbidden =>
            new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à créer un encaissement."),
        CreateCustomerPaymentFailureReason.InvalidClient =>
            new(PaymentFailureKind.NotFound, "Le client sélectionné n'est plus disponible."),
        CreateCustomerPaymentFailureReason.InvalidAmount =>
            new(PaymentFailureKind.Validation, "Le montant du paiement n'est pas valide."),
        CreateCustomerPaymentFailureReason.InvalidPaymentMethod =>
            new(PaymentFailureKind.Validation, "Le mode de paiement sélectionné n'est pas valide."),
        CreateCustomerPaymentFailureReason.MissingAllocations or
        CreateCustomerPaymentFailureReason.InvalidAllocation or
        CreateCustomerPaymentFailureReason.DuplicateAllocationTarget or
        CreateCustomerPaymentFailureReason.AllocationExceedsOutstanding or
        CreateCustomerPaymentFailureReason.AllocationsExceedPayment =>
            new(PaymentFailureKind.Validation, "Vérifiez les montants affectés aux obligations."),
        CreateCustomerPaymentFailureReason.ObligationNotFound or
        CreateCustomerPaymentFailureReason.ObligationBelongsToDifferentClient =>
            new(PaymentFailureKind.Conflict, "Une obligation sélectionnée n'est plus disponible pour ce client. Actualisez avant de réessayer."),
        _ => new(PaymentFailureKind.Technical, "Impossible d'enregistrer l'encaissement.")
    };

    private static Failure MapAdvanceFailure(ApplyCustomerAdvanceFailureReason reason) => reason switch
    {
        ApplyCustomerAdvanceFailureReason.NotAuthenticated =>
            new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
        ApplyCustomerAdvanceFailureReason.Forbidden =>
            new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à affecter cette avance."),
        ApplyCustomerAdvanceFailureReason.SourcePaymentNotFound =>
            new(PaymentFailureKind.NotFound, "Le paiement source n'existe plus."),
        ApplyCustomerAdvanceFailureReason.SourcePaymentNotEligible or
        ApplyCustomerAdvanceFailureReason.NoAvailableAdvance =>
            new(PaymentFailureKind.Conflict, "Cette avance n'est plus disponible. Actualisez le paiement."),
        ApplyCustomerAdvanceFailureReason.MissingAllocations or
        ApplyCustomerAdvanceFailureReason.InvalidAllocation or
        ApplyCustomerAdvanceFailureReason.DuplicateAllocationTarget or
        ApplyCustomerAdvanceFailureReason.AllocationExceedsOutstanding or
        ApplyCustomerAdvanceFailureReason.AllocationsExceedAvailableAdvance =>
            new(PaymentFailureKind.Validation, "Vérifiez les montants affectés."),
        ApplyCustomerAdvanceFailureReason.ObligationNotFound or
        ApplyCustomerAdvanceFailureReason.ObligationBelongsToDifferentClient =>
            new(PaymentFailureKind.Conflict, "Une obligation sélectionnée n'est plus disponible. Actualisez avant de réessayer."),
        _ => new(PaymentFailureKind.Validation, "Le paiement source n'est pas valide.")
    };

    private sealed record Failure(PaymentFailureKind Kind, string Message);
}
