namespace FinancialManagement.Application.InitialCustomerReceivables;

public sealed record InitialCustomerReceivableListQuery(int PageNumber = 1, int PageSize = 50, long? ClientId = null);
