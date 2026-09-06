namespace FinancialManagement.Application.Clients;

public interface IClientService
{
    Task<CreateClientResult> CreateAsync(
        CreateClientCommand command,
        CancellationToken cancellationToken = default);

    Task<ClientListResult> GetPageAsync(
        ClientListQuery query,
        CancellationToken cancellationToken = default);

    Task<ClientDetailResult> GetByIdAsync(
        long clientId,
        CancellationToken cancellationToken = default);

    Task<UpdateClientResult> UpdateAsync(
        UpdateClientCommand command,
        CancellationToken cancellationToken = default);

    Task<DeleteClientResult> DeleteAsync(
        long clientId,
        CancellationToken cancellationToken = default);
}
