namespace FinancialManagement.Application.Purchases;

public interface IPurchaseService
{
    Task<CreatePurchaseResult> CreateAsync(
        CreatePurchaseCommand command,
        CancellationToken cancellationToken = default);
}
