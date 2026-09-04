namespace FinancialManagement.Application.CustomerAdvances;

public sealed record ApplyCustomerAdvanceCommand(
    long CustomerPaymentId,
    IReadOnlyCollection<CustomerAdvanceAllocationInput>? Allocations);
