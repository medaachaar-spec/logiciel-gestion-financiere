using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.InitialSupplierPayables;
using FinancialManagement.Application.Suppliers;
using FinancialManagement.Desktop.Services;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class InitialSupplierPayablesViewModel : InitialObligationViewModelBase
{
    private readonly IInitialSupplierPayableService _payableService;
    private readonly ISupplierLookupService _supplierLookupService;

    public InitialSupplierPayablesViewModel(
        IInitialSupplierPayableService payableService,
        ISupplierLookupService supplierLookupService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator)
        : base(
            currentUserSession,
            authorizationService,
            operationCoordinator,
            "Dettes initiales",
            "Saisissez uniquement les dettes fournisseurs antérieures à l'utilisation de l'application.",
            "Fournisseur",
            "Date de dette",
            "Aucune dette initiale enregistrée.")
    {
        ArgumentNullException.ThrowIfNull(payableService);
        ArgumentNullException.ThrowIfNull(supplierLookupService);
        _payableService = payableService;
        _supplierLookupService = supplierLookupService;
    }

    protected override async Task<InitialObligationPageResponse> GetPageAsync(
        int pageNumber,
        int pageSize,
        long? partyId,
        CancellationToken cancellationToken)
    {
        var result = await _payableService.GetPageAsync(
            new InitialSupplierPayableListQuery(pageNumber, pageSize, partyId),
            cancellationToken);
        if (!result.Success)
        {
            return new(
                false,
                result.FailureReason is InitialSupplierPayableListFailureReason.NotAuthenticated
                    or InitialSupplierPayableListFailureReason.Forbidden,
                result.FailureReason == InitialSupplierPayableListFailureReason.DataIntegrityViolation
                    ? "Les dettes initiales contiennent des données incohérentes. Contactez l'administrateur."
                    : "Impossible de charger les dettes initiales.",
                [],
                pageNumber,
                0,
                0);
        }

        var items = result.Items.Select(item => new InitialObligationListItemViewModel(
            item.Id,
            item.Reference,
            item.SupplierName,
            item.PayableDate,
            item.InitialAmountMinor,
            item.OutstandingAmountMinor,
            item.DueDate,
            item.Observation)).ToArray();
        return new(true, false, null, items, result.PageNumber, result.TotalPages, result.TotalCount);
    }

    protected override async Task<InitialObligationPartyLookupResponse> SearchPartiesAsync(
        string? searchText,
        int limit,
        CancellationToken cancellationToken)
    {
        var result = await _supplierLookupService.SearchAsync(
            new SupplierLookupQuery(searchText, limit),
            cancellationToken);
        return result.Success
            ? new(true, false, null, result.Items.Select(item =>
                new InitialObligationPartyItemViewModel(item.SupplierId, item.DisplayName, item.Identifier)).ToArray())
            : new(
                false,
                result.FailureReason is SupplierLookupFailureReason.NotAuthenticated or SupplierLookupFailureReason.Forbidden,
                "Impossible de rechercher les fournisseurs.",
                []);
    }

    protected override async Task<InitialObligationCreateResponse> CreateAsync(
        long partyId,
        long amountMinor,
        DateOnly businessDate,
        DateOnly? dueDate,
        string? observation,
        CancellationToken cancellationToken)
    {
        var result = await _payableService.CreateAsync(
            new CreateInitialSupplierPayableCommand(
                partyId,
                amountMinor,
                businessDate,
                dueDate,
                observation),
            cancellationToken);
        return result.Success
            ? new(true, false, null, result.Reference)
            : new(
                false,
                result.FailureReason is CreateInitialSupplierPayableFailureReason.NotAuthenticated
                    or CreateInitialSupplierPayableFailureReason.Forbidden,
                result.FailureReason switch
                {
                    CreateInitialSupplierPayableFailureReason.InvalidSupplier => "Le fournisseur sélectionné est invalide.",
                    CreateInitialSupplierPayableFailureReason.InvalidAmount => "Le montant initial est invalide.",
                    _ => "Impossible d'enregistrer la dette initiale."
                },
                null);
    }
}
