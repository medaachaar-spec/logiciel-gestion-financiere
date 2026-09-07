using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Clients;
using FinancialManagement.Application.InitialCustomerReceivables;
using FinancialManagement.Desktop.Services;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class InitialCustomerReceivablesViewModel : InitialObligationViewModelBase
{
    private readonly IInitialCustomerReceivableService _receivableService;
    private readonly IClientLookupService _clientLookupService;

    public InitialCustomerReceivablesViewModel(
        IInitialCustomerReceivableService receivableService,
        IClientLookupService clientLookupService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator)
        : base(
            currentUserSession,
            authorizationService,
            operationCoordinator,
            "Créances initiales",
            "Saisissez uniquement les créances clients antérieures à l'utilisation de l'application.",
            "Client",
            "Date de créance",
            "Aucune créance initiale enregistrée.")
    {
        ArgumentNullException.ThrowIfNull(receivableService);
        ArgumentNullException.ThrowIfNull(clientLookupService);
        _receivableService = receivableService;
        _clientLookupService = clientLookupService;
    }

    protected override async Task<InitialObligationPageResponse> GetPageAsync(
        int pageNumber,
        int pageSize,
        long? partyId,
        CancellationToken cancellationToken)
    {
        var result = await _receivableService.GetPageAsync(
            new InitialCustomerReceivableListQuery(pageNumber, pageSize, partyId),
            cancellationToken);
        if (!result.Success)
        {
            return new(
                false,
                result.FailureReason is InitialCustomerReceivableListFailureReason.NotAuthenticated
                    or InitialCustomerReceivableListFailureReason.Forbidden,
                result.FailureReason == InitialCustomerReceivableListFailureReason.DataIntegrityViolation
                    ? "Les créances initiales contiennent des données incohérentes. Contactez l'administrateur."
                    : "Impossible de charger les créances initiales.",
                [],
                pageNumber,
                0,
                0);
        }

        var items = result.Items.Select(item => new InitialObligationListItemViewModel(
            item.Id,
            item.Reference,
            item.ClientName,
            item.ReceivableDate,
            item.InitialAmountMinor,
            outstandingAmountMinor: item.OutstandingAmountMinor,
            dueDate: item.DueDate,
            observation: item.Observation)).ToArray();
        return new(true, false, null, items, result.PageNumber, result.TotalPages, result.TotalCount);
    }

    protected override async Task<InitialObligationPartyLookupResponse> SearchPartiesAsync(
        string? searchText,
        int limit,
        CancellationToken cancellationToken)
    {
        var result = await _clientLookupService.SearchAsync(
            new ClientLookupQuery(searchText, limit),
            cancellationToken);
        return result.Success
            ? new(true, false, null, result.Items.Select(item =>
                new InitialObligationPartyItemViewModel(item.ClientId, item.DisplayName, item.Identifier)).ToArray())
            : new(
                false,
                result.FailureReason is ClientLookupFailureReason.NotAuthenticated or ClientLookupFailureReason.Forbidden,
                "Impossible de rechercher les clients.",
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
        var result = await _receivableService.CreateAsync(
            new CreateInitialCustomerReceivableCommand(
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
                result.FailureReason is CreateInitialCustomerReceivableFailureReason.NotAuthenticated
                    or CreateInitialCustomerReceivableFailureReason.Forbidden,
                result.FailureReason switch
                {
                    CreateInitialCustomerReceivableFailureReason.InvalidClient => "Le client sélectionné est invalide.",
                    CreateInitialCustomerReceivableFailureReason.InvalidAmount => "Le montant initial est invalide.",
                    _ => "Impossible d'enregistrer la créance initiale."
                },
                null);
    }
}
