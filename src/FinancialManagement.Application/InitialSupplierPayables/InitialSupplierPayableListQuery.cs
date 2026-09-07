namespace FinancialManagement.Application.InitialSupplierPayables;

public sealed record InitialSupplierPayableListQuery(int PageNumber = 1, int PageSize = 50, long? SupplierId = null);
