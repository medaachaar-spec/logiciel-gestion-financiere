namespace FinancialManagement.Application.Initialization;

public interface IApplicationInitializationService
{
    Task<ApplicationInitializationState> GetStateAsync(CancellationToken cancellationToken = default);
    Task<BootstrapResponsibleResult> BootstrapResponsibleAsync(
        BootstrapResponsibleCommand command, CancellationToken cancellationToken = default);
}
