namespace FinancialManagement.Application.Sales;

public interface IBusinessReferenceGenerator
{
    string GenerateSaleReference(DateOnly businessDate);

    string GenerateCustomerPaymentReference(DateOnly businessDate);
}
