namespace FinancialManagement.Application.SupplierAdvances;

public interface ISupplierAdvanceService
{
    Task<ApplySupplierAdvanceResult> ApplyAsync(
        ApplySupplierAdvanceCommand command,
        CancellationToken cancellationToken = default);
}
