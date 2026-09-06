namespace FinancialManagement.Application.Sales;

public interface IBusinessReferenceGenerator
{
    string GenerateSaleReference(DateOnly businessDate);

    string GenerateCustomerPaymentReference(DateOnly businessDate);

    string GeneratePurchaseReference(DateOnly businessDate);

    string GenerateSupplierPaymentReference(DateOnly businessDate);

    string GenerateExpenseReference(DateOnly businessDate);

    string GenerateInitialCustomerReceivableReference(DateOnly historicalDate);

    string GenerateInitialSupplierPayableReference(DateOnly historicalDate);

    string GenerateTreasuryTransferReference(DateOnly businessDate);
}
