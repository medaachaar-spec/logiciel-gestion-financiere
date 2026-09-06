namespace FinancialManagement.Application.Clients;

public interface IClientLookupService
{
    Task<ClientLookupResult> SearchAsync(
        ClientLookupQuery query,
        CancellationToken cancellationToken = default);
}
