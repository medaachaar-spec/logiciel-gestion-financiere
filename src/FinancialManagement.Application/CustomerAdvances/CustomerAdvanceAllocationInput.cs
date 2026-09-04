namespace FinancialManagement.Application.CustomerAdvances;

public sealed record CustomerAdvanceAllocationInput(
    long? SaleId,
    long? InitialCustomerReceivableId,
    long AmountMinor);
