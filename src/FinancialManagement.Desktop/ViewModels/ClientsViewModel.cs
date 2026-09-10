using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Clients;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class ClientsViewModel : PartyManagementViewModelBase
{
    private readonly IClientService _clientService;

    public ClientsViewModel(
        IClientService clientService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator)
        : base(
            currentUserSession,
            authorizationService,
            operationCoordinator,
            PermissionModule.Clients,
            "client",
            "Client",
            "clients",
            "Clients",
            "Consultez et gérez les informations de vos clients.",
            "Créance actuelle")
    {
        ArgumentNullException.ThrowIfNull(clientService);
        _clientService = clientService;
    }

    protected override async Task<PartyPageResult> GetPageFromBackendAsync(
        int pageNumber,
        int pageSize,
        string? searchText,
        CancellationToken cancellationToken)
    {
        var result = await _clientService.GetPageAsync(
            new ClientListQuery(pageNumber, pageSize, searchText),
            cancellationToken);
        if (!result.Success)
        {
            return PartyPageResult.Failed(Map(result.FailureReason), pageNumber);
        }

        var items = result.Items
            .Select(item => new PartyListItemViewModel(
                item.Id,
                item.Name,
                item.Phone,
                item.Company,
                item.Identifier,
                item.CurrentReceivableMinorUnits,
                "Client",
                "créance actuelle"))
            .ToArray();
        return new PartyPageResult(
            true,
            PartyFailureReason.None,
            items,
            result.PageNumber,
            result.TotalPages,
            result.TotalCount);
    }

    protected override async Task<PartyDetailResult> GetDetailFromBackendAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _clientService.GetByIdAsync(id, cancellationToken);
        if (!result.Success || result.Item is null)
        {
            return PartyDetailResult.Failed(Map(result.FailureReason));
        }

        var item = result.Item;
        return new PartyDetailResult(
            true,
            PartyFailureReason.None,
            new PartyDetailData(
                item.Id,
                item.Name,
                item.Phone,
                item.Address,
                item.Company,
                item.Identifier,
                item.Observation));
    }

    protected override async Task<PartyMutationResult> CreateOnBackendAsync(
        PartyFormData form,
        CancellationToken cancellationToken)
    {
        var result = await _clientService.CreateAsync(
            new CreateClientCommand(
                form.Name,
                form.Phone,
                form.Address,
                form.Company,
                form.Identifier,
                form.Observation),
            cancellationToken);
        return result.Success
            ? PartyMutationResult.Succeeded()
            : PartyMutationResult.Failed(Map(result.FailureReason));
    }

    protected override async Task<PartyMutationResult> UpdateOnBackendAsync(
        long id,
        PartyFormData form,
        CancellationToken cancellationToken)
    {
        var result = await _clientService.UpdateAsync(
            new UpdateClientCommand(
                id,
                form.Name,
                form.Phone,
                form.Address,
                form.Company,
                form.Identifier,
                form.Observation),
            cancellationToken);
        return result.Success
            ? PartyMutationResult.Succeeded()
            : PartyMutationResult.Failed(Map(result.FailureReason));
    }

    protected override async Task<PartyMutationResult> DeleteOnBackendAsync(
        long id,
        CancellationToken cancellationToken)
    {
        var result = await _clientService.DeleteAsync(id, cancellationToken);
        return result.Success
            ? PartyMutationResult.Succeeded()
            : PartyMutationResult.Failed(Map(result.FailureReason));
    }

    private static PartyFailureReason Map(ClientListFailureReason reason) => reason switch
    {
        ClientListFailureReason.NotAuthenticated => PartyFailureReason.NotAuthenticated,
        ClientListFailureReason.Forbidden => PartyFailureReason.Forbidden,
        ClientListFailureReason.InvalidPageNumber => PartyFailureReason.InvalidPageNumber,
        ClientListFailureReason.InvalidPageSize => PartyFailureReason.InvalidPageSize,
        ClientListFailureReason.DataIntegrityViolation => PartyFailureReason.Technical,
        _ => PartyFailureReason.Technical
    };

    private static PartyFailureReason Map(ClientDetailFailureReason reason) => reason switch
    {
        ClientDetailFailureReason.NotAuthenticated => PartyFailureReason.NotAuthenticated,
        ClientDetailFailureReason.Forbidden => PartyFailureReason.Forbidden,
        ClientDetailFailureReason.InvalidClientId => PartyFailureReason.InvalidId,
        ClientDetailFailureReason.NotFound => PartyFailureReason.NotFound,
        _ => PartyFailureReason.Technical
    };

    private static PartyFailureReason Map(CreateClientFailureReason reason) => reason switch
    {
        CreateClientFailureReason.NotAuthenticated => PartyFailureReason.NotAuthenticated,
        CreateClientFailureReason.NotAuthorized => PartyFailureReason.NotAuthorized,
        CreateClientFailureReason.InvalidName => PartyFailureReason.InvalidName,
        _ => PartyFailureReason.Technical
    };

    private static PartyFailureReason Map(UpdateClientFailureReason reason) => reason switch
    {
        UpdateClientFailureReason.NotAuthenticated => PartyFailureReason.NotAuthenticated,
        UpdateClientFailureReason.NotAuthorized => PartyFailureReason.NotAuthorized,
        UpdateClientFailureReason.InvalidClientId => PartyFailureReason.InvalidId,
        UpdateClientFailureReason.InvalidName => PartyFailureReason.InvalidName,
        UpdateClientFailureReason.NotFound => PartyFailureReason.NotFound,
        _ => PartyFailureReason.Technical
    };

    private static PartyFailureReason Map(DeleteClientFailureReason reason) => reason switch
    {
        DeleteClientFailureReason.NotAuthenticated => PartyFailureReason.NotAuthenticated,
        DeleteClientFailureReason.NotAuthorized => PartyFailureReason.NotAuthorized,
        DeleteClientFailureReason.InvalidClientId => PartyFailureReason.InvalidId,
        DeleteClientFailureReason.NotFound => PartyFailureReason.NotFound,
        DeleteClientFailureReason.InUse => PartyFailureReason.InUse,
        _ => PartyFailureReason.Technical
    };
}
