namespace FinancialManagement.Application.Sales;

public interface ISaleService
{
    Task<SaleListResult> GetPageAsync(
        SaleListQuery query,
        CancellationToken cancellationToken = default);

    Task<CreateSaleResult> CreateAsync(
        CreateSaleCommand command,
        CancellationToken cancellationToken = default);
}
