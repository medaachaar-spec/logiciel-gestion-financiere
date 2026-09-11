using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.SupplierAdvances;
using FinancialManagement.Application.SupplierPayments;
using FinancialManagement.Application.Suppliers;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class SupplierPaymentsViewModel : PaymentManagementViewModelBase
{
    private readonly ISupplierPaymentService _paymentService;
    private readonly ISupplierPaymentObligationReadService _obligationReadService;
    private readonly ISupplierPaymentObligationLookupService _obligationLookupService;
    private readonly ISupplierAdvanceService _advanceService;
    private readonly ISupplierLookupService _supplierLookupService;

    public SupplierPaymentsViewModel(
        ISupplierPaymentService paymentService,
        ISupplierPaymentObligationReadService obligationReadService,
        ISupplierPaymentObligationLookupService obligationLookupService,
        ISupplierAdvanceService advanceService,
        ISupplierLookupService supplierLookupService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator)
        : base(
            currentUserSession,
            authorizationService,
            operationCoordinator,
            PermissionModule.SupplierPayments,
            PermissionModule.Suppliers,
            "Paiements fournisseurs",
            "Suivez chaque dette, son règlement et l'historique de ses paiements.",
            "fournisseur",
            "Fournisseur",
            "paiements")
    {
        ArgumentNullException.ThrowIfNull(paymentService);
        ArgumentNullException.ThrowIfNull(obligationReadService);
        ArgumentNullException.ThrowIfNull(obligationLookupService);
        ArgumentNullException.ThrowIfNull(advanceService);
        ArgumentNullException.ThrowIfNull(supplierLookupService);

        _paymentService = paymentService;
        _obligationReadService = obligationReadService;
        _obligationLookupService = obligationLookupService;
        _advanceService = advanceService;
        _supplierLookupService = supplierLookupService;
    }

    protected override string SingularPaymentLower => "paiement";

    internal override async Task<PaymentPageData> GetPageAsync(
        PaymentListQueryData query,
        CancellationToken cancellationToken)
    {
        var applicationQuery = new SupplierPaymentObligationPageQuery(
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
                item.SupplierId,
                item.SupplierName,
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
                item.SupplierId,
                item.SupplierName,
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
                item.SupplierId,
                item.SupplierName,
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

    internal override async Task<PartyLookupResponse> SearchPartiesAsync(
        string? searchText,
        int limit,
        CancellationToken cancellationToken)
    {
        var result = await _supplierLookupService.SearchAsync(
            new SupplierLookupQuery(searchText, limit),
            cancellationToken);
        if (!result.Success)
        {
            var failure = result.FailureReason switch
            {
                SupplierLookupFailureReason.NotAuthenticated =>
                    new Failure(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
                SupplierLookupFailureReason.Forbidden =>
                    new Failure(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à consulter les fournisseurs."),
                _ => new Failure(PaymentFailureKind.Validation, "La recherche de fournisseurs n'est pas valide.")
            };
            return new PartyLookupResponse(false, failure.Kind, failure.Message, []);
        }

        return new PartyLookupResponse(
            true,
            PaymentFailureKind.None,
            null,
            result.Items.Select(item => new PartyLookupData(
                item.SupplierId,
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
        var query = new SupplierPaymentObligationLookupQuery(
            partyId,
            applyAdvance
                ? SupplierPaymentObligationLookupPurpose.ApplyAdvance
                : SupplierPaymentObligationLookupPurpose.CreatePayment,
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
        var command = new CreateSupplierPaymentCommand(
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
            result.SupplierPaymentId,
            result.SupplierPaymentReference);
    }

    internal override async Task<AdvanceApplyResponse> ApplyAdvanceAsync(
        long paymentId,
        IReadOnlyList<PaymentAllocationRequest> allocations,
        CancellationToken cancellationToken)
    {
        var command = new ApplySupplierAdvanceCommand(
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

    private static SupplierPaymentAllocationInput MapCreateAllocation(PaymentAllocationRequest allocation) =>
        allocation.TargetKind switch
        {
            PaymentTargetKind.Purchase => new SupplierPaymentAllocationInput(
                allocation.TargetId,
                null,
                allocation.AmountMinor),
            PaymentTargetKind.InitialSupplierPayable => new SupplierPaymentAllocationInput(
                null,
                allocation.TargetId,
                allocation.AmountMinor),
            _ => throw new InvalidOperationException("Unsupported supplier allocation target.")
        };

    private static SupplierAdvanceAllocationInput MapAdvanceAllocation(PaymentAllocationRequest allocation) =>
        allocation.TargetKind switch
        {
            PaymentTargetKind.Purchase => new SupplierAdvanceAllocationInput(
                allocation.TargetId,
                null,
                allocation.AmountMinor),
            PaymentTargetKind.InitialSupplierPayable => new SupplierAdvanceAllocationInput(
                null,
                allocation.TargetId,
                allocation.AmountMinor),
            _ => throw new InvalidOperationException("Unsupported supplier advance target.")
        };

    private static PaymentTargetKind MapTarget(SupplierPaymentAllocationTargetType targetType) => targetType switch
    {
        SupplierPaymentAllocationTargetType.Purchase => PaymentTargetKind.Purchase,
        SupplierPaymentAllocationTargetType.InitialSupplierPayable =>
            PaymentTargetKind.InitialSupplierPayable,
        _ => throw new InvalidOperationException("Unsupported supplier payment target.")
    };

    private static PaymentTargetKind MapTarget(SupplierPaymentObligationTargetType targetType) => targetType switch
    {
        SupplierPaymentObligationTargetType.Purchase => PaymentTargetKind.Purchase,
        SupplierPaymentObligationTargetType.InitialSupplierPayable =>
            PaymentTargetKind.InitialSupplierPayable,
        _ => throw new InvalidOperationException("Unsupported supplier obligation target.")
    };

    private static SupplierPaymentObligationTargetType MapReadTarget(PaymentTargetKind targetKind) =>
        targetKind switch
        {
            PaymentTargetKind.Purchase => SupplierPaymentObligationTargetType.Purchase,
            PaymentTargetKind.InitialSupplierPayable =>
                SupplierPaymentObligationTargetType.InitialSupplierPayable,
            _ => throw new InvalidOperationException("Unsupported supplier obligation target.")
        };

    private static Failure MapObligationReadFailure(SupplierPaymentObligationReadFailureReason reason) =>
        reason switch
        {
            SupplierPaymentObligationReadFailureReason.NotAuthenticated =>
                new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
            SupplierPaymentObligationReadFailureReason.Forbidden =>
                new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à consulter les paiements fournisseurs."),
            SupplierPaymentObligationReadFailureReason.InvalidPageNumber
                or SupplierPaymentObligationReadFailureReason.InvalidPageSize
                or SupplierPaymentObligationReadFailureReason.InvalidDateRange
                or SupplierPaymentObligationReadFailureReason.InvalidStatus
                or SupplierPaymentObligationReadFailureReason.InvalidTarget =>
                new(PaymentFailureKind.Validation, "Les filtres ou l'obligation demandée ne sont pas valides."),
            SupplierPaymentObligationReadFailureReason.NotFound =>
                new(PaymentFailureKind.NotFound, "Cette obligation n'existe plus."),
            _ => new(PaymentFailureKind.DataIntegrity, "Les données d'affectation sont incohérentes.")
        };

    private static Failure MapListFailure(SupplierPaymentListFailureReason reason) => reason switch
    {
        SupplierPaymentListFailureReason.NotAuthenticated =>
            new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
        SupplierPaymentListFailureReason.Forbidden =>
            new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à consulter les paiements fournisseurs."),
        SupplierPaymentListFailureReason.InvalidDateRange =>
            new(PaymentFailureKind.Validation, "La période sélectionnée n'est pas valide."),
        SupplierPaymentListFailureReason.InvalidPaymentMethod =>
            new(PaymentFailureKind.Validation, "Le mode de paiement sélectionné n'est pas valide."),
        SupplierPaymentListFailureReason.InvalidPageNumber or SupplierPaymentListFailureReason.InvalidPageSize =>
            new(PaymentFailureKind.Validation, "La page demandée n'est pas valide."),
        _ => new(PaymentFailureKind.DataIntegrity, "Les données de paiement sont incohérentes. Actualisez avant de réessayer.")
    };

    private static Failure MapDetailFailure(SupplierPaymentDetailFailureReason reason) => reason switch
    {
        SupplierPaymentDetailFailureReason.NotAuthenticated =>
            new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
        SupplierPaymentDetailFailureReason.Forbidden =>
            new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à consulter ce paiement."),
        SupplierPaymentDetailFailureReason.NotFound =>
            new(PaymentFailureKind.NotFound, "Ce paiement n'existe plus. Actualisez la liste."),
        SupplierPaymentDetailFailureReason.InvalidPaymentId =>
            new(PaymentFailureKind.Validation, "Le paiement sélectionné n'est pas valide."),
        _ => new(PaymentFailureKind.DataIntegrity, "Le détail du paiement est incohérent. Actualisez avant de réessayer.")
    };

    private static Failure MapObligationFailure(SupplierPaymentObligationLookupFailureReason reason) => reason switch
    {
        SupplierPaymentObligationLookupFailureReason.NotAuthenticated =>
            new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
        SupplierPaymentObligationLookupFailureReason.Forbidden =>
            new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à consulter les obligations de ce fournisseur."),
        SupplierPaymentObligationLookupFailureReason.SupplierNotFound =>
            new(PaymentFailureKind.NotFound, "Le fournisseur sélectionné n'est plus disponible."),
        SupplierPaymentObligationLookupFailureReason.DataIntegrityViolation =>
            new(PaymentFailureKind.DataIntegrity, "Les obligations ont changé ou sont incohérentes. Actualisez avant de réessayer."),
        _ => new(PaymentFailureKind.Validation, "La recherche d'obligations n'est pas valide.")
    };

    private static Failure MapCreateFailure(CreateSupplierPaymentFailureReason reason) => reason switch
    {
        CreateSupplierPaymentFailureReason.NotAuthenticated =>
            new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
        CreateSupplierPaymentFailureReason.Forbidden =>
            new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à créer un paiement fournisseur."),
        CreateSupplierPaymentFailureReason.InvalidSupplier =>
            new(PaymentFailureKind.NotFound, "Le fournisseur sélectionné n'est plus disponible."),
        CreateSupplierPaymentFailureReason.InvalidAmount =>
            new(PaymentFailureKind.Validation, "Le montant du paiement n'est pas valide."),
        CreateSupplierPaymentFailureReason.InvalidPaymentMethod =>
            new(PaymentFailureKind.Validation, "Le mode de paiement sélectionné n'est pas valide."),
        CreateSupplierPaymentFailureReason.MissingAllocations or
        CreateSupplierPaymentFailureReason.InvalidAllocation or
        CreateSupplierPaymentFailureReason.DuplicateAllocationTarget or
        CreateSupplierPaymentFailureReason.AllocationExceedsOutstanding or
        CreateSupplierPaymentFailureReason.AllocationsExceedPayment =>
            new(PaymentFailureKind.Validation, "Vérifiez les montants affectés aux obligations."),
        CreateSupplierPaymentFailureReason.ObligationNotFound or
        CreateSupplierPaymentFailureReason.ObligationBelongsToDifferentSupplier =>
            new(PaymentFailureKind.Conflict, "Une obligation sélectionnée n'est plus disponible pour ce fournisseur. Actualisez avant de réessayer."),
        _ => new(PaymentFailureKind.Technical, "Impossible d'enregistrer le paiement fournisseur.")
    };

    private static Failure MapAdvanceFailure(ApplySupplierAdvanceFailureReason reason) => reason switch
    {
        ApplySupplierAdvanceFailureReason.NotAuthenticated =>
            new(PaymentFailureKind.NotAuthenticated, "Votre session n'est plus active."),
        ApplySupplierAdvanceFailureReason.Forbidden =>
            new(PaymentFailureKind.Forbidden, "Vous n'êtes pas autorisé à affecter cette avance."),
        ApplySupplierAdvanceFailureReason.SourcePaymentNotFound =>
            new(PaymentFailureKind.NotFound, "Le paiement source n'existe plus."),
        ApplySupplierAdvanceFailureReason.NoAvailableAdvance =>
            new(PaymentFailureKind.Conflict, "Cette avance n'est plus disponible. Actualisez le paiement."),
        ApplySupplierAdvanceFailureReason.MissingAllocations or
        ApplySupplierAdvanceFailureReason.InvalidAllocation or
        ApplySupplierAdvanceFailureReason.DuplicateAllocationTarget or
        ApplySupplierAdvanceFailureReason.AllocationExceedsOutstanding or
        ApplySupplierAdvanceFailureReason.AllocationsExceedAvailableAdvance =>
            new(PaymentFailureKind.Validation, "Vérifiez les montants affectés."),
        ApplySupplierAdvanceFailureReason.ObligationNotFound or
        ApplySupplierAdvanceFailureReason.ObligationBelongsToDifferentSupplier =>
            new(PaymentFailureKind.Conflict, "Une obligation sélectionnée n'est plus disponible. Actualisez avant de réessayer."),
        _ => new(PaymentFailureKind.Validation, "Le paiement source n'est pas valide.")
    };

    private sealed record Failure(PaymentFailureKind Kind, string Message);
}
