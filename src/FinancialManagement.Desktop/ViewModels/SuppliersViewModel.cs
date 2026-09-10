using FinancialManagement.Application.Authentication;
using FinancialManagement.Application.Authorization;
using FinancialManagement.Application.Suppliers;
using FinancialManagement.Desktop.Services;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class SuppliersViewModel : PartyManagementViewModelBase
{
    private readonly ISupplierService _supplierService;

    public SuppliersViewModel(
        ISupplierService supplierService,
        ICurrentUserSession currentUserSession,
        IAuthorizationService authorizationService,
        DashboardOperationCoordinator operationCoordinator)
        : base(
            currentUserSession,
            authorizationService,
            operationCoordinator,
            PermissionModule.Suppliers,
            "fournisseur",
            "Fournisseur",
            "fournisseurs",
            "Fournisseurs",
            "Consultez et gérez les informations de vos fournisseurs.",
            "Dette actuelle")
    {
        ArgumentNullException.ThrowIfNull(supplierService);
        _supplierService = supplierService;
    }

    protected override async Task<PartyPageResult> GetPageFromBackendAsync(
        int pageNumber,
        int pageSize,
        string? searchText,
        CancellationToken cancellationToken)
    {
        var result = await _supplierService.GetPageAsync(
            new SupplierListQuery(pageNumber, pageSize, searchText),
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
                item.CurrentPayableMinorUnits,
                "Fournisseur",
                "dette actuelle"))
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
        var result = await _supplierService.GetByIdAsync(id, cancellationToken);
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
        var result = await _supplierService.CreateAsync(
            new CreateSupplierCommand(
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
        var result = await _supplierService.UpdateAsync(
            new UpdateSupplierCommand(
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
        var result = await _supplierService.DeleteAsync(id, cancellationToken);
        return result.Success
            ? PartyMutationResult.Succeeded()
            : PartyMutationResult.Failed(Map(result.FailureReason));
    }

    private static PartyFailureReason Map(SupplierListFailureReason reason) => reason switch
    {
        SupplierListFailureReason.NotAuthenticated => PartyFailureReason.NotAuthenticated,
        SupplierListFailureReason.Forbidden => PartyFailureReason.Forbidden,
        SupplierListFailureReason.InvalidPageNumber => PartyFailureReason.InvalidPageNumber,
        SupplierListFailureReason.InvalidPageSize => PartyFailureReason.InvalidPageSize,
        SupplierListFailureReason.DataIntegrityViolation => PartyFailureReason.Technical,
        _ => PartyFailureReason.Technical
    };

    private static PartyFailureReason Map(SupplierDetailFailureReason reason) => reason switch
    {
        SupplierDetailFailureReason.NotAuthenticated => PartyFailureReason.NotAuthenticated,
        SupplierDetailFailureReason.Forbidden => PartyFailureReason.Forbidden,
        SupplierDetailFailureReason.InvalidSupplierId => PartyFailureReason.InvalidId,
        SupplierDetailFailureReason.NotFound => PartyFailureReason.NotFound,
        _ => PartyFailureReason.Technical
    };

    private static PartyFailureReason Map(CreateSupplierFailureReason reason) => reason switch
    {
        CreateSupplierFailureReason.NotAuthenticated => PartyFailureReason.NotAuthenticated,
        CreateSupplierFailureReason.NotAuthorized => PartyFailureReason.NotAuthorized,
        CreateSupplierFailureReason.InvalidName => PartyFailureReason.InvalidName,
        _ => PartyFailureReason.Technical
    };

    private static PartyFailureReason Map(UpdateSupplierFailureReason reason) => reason switch
    {
        UpdateSupplierFailureReason.NotAuthenticated => PartyFailureReason.NotAuthenticated,
        UpdateSupplierFailureReason.NotAuthorized => PartyFailureReason.NotAuthorized,
        UpdateSupplierFailureReason.InvalidSupplierId => PartyFailureReason.InvalidId,
        UpdateSupplierFailureReason.InvalidName => PartyFailureReason.InvalidName,
        UpdateSupplierFailureReason.NotFound => PartyFailureReason.NotFound,
        _ => PartyFailureReason.Technical
    };

    private static PartyFailureReason Map(DeleteSupplierFailureReason reason) => reason switch
    {
        DeleteSupplierFailureReason.NotAuthenticated => PartyFailureReason.NotAuthenticated,
        DeleteSupplierFailureReason.NotAuthorized => PartyFailureReason.NotAuthorized,
        DeleteSupplierFailureReason.InvalidSupplierId => PartyFailureReason.InvalidId,
        DeleteSupplierFailureReason.NotFound => PartyFailureReason.NotFound,
        DeleteSupplierFailureReason.InUse => PartyFailureReason.InUse,
        _ => PartyFailureReason.Technical
    };
}
