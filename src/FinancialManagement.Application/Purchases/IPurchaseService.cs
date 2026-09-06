namespace FinancialManagement.Application.Purchases;

public interface IPurchaseService
{
    Task<PurchaseListResult> GetPageAsync(
        PurchaseListQuery query,
        CancellationToken cancellationToken = default);

    Task<CreatePurchaseResult> CreateAsync(
        CreatePurchaseCommand command,
        CancellationToken cancellationToken = default);
}
