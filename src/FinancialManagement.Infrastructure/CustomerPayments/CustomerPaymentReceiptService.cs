using FinancialManagement.Application.CustomerPayments;

namespace FinancialManagement.Infrastructure.CustomerPayments;

internal sealed class CustomerPaymentReceiptService(
    FinancialManagementDbContext dbContext,
    ICustomerPaymentService paymentService,
    ICustomerPaymentObligationReadService obligationReadService) : ICustomerPaymentReceiptService
{
    public Task<CustomerPaymentReceiptResult> GetAsync(
        long customerPaymentId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return SqliteReadSnapshot.ExecuteAsync(
            dbContext,
            token => GetSnapshotAsync(customerPaymentId, token),
            cancellationToken);
    }

    private async Task<CustomerPaymentReceiptResult> GetSnapshotAsync(
        long customerPaymentId,
        CancellationToken cancellationToken)
    {
        var paymentResult = await paymentService.GetByIdAsync(customerPaymentId, cancellationToken);
        if (!paymentResult.Success || paymentResult.Item is null)
            return CustomerPaymentReceiptResult.Failed(Map(paymentResult.FailureReason));

        var payment = paymentResult.Item;
        var receiptAllocations = new List<CustomerPaymentReceiptAllocationItem>(payment.Allocations.Count);
        DateTimeOffset? paymentCreatedAtUtc = null;

        foreach (var allocation in payment.Allocations.OrderBy(item => item.Id))
        {
            var targetType = allocation.TargetType switch
            {
                CustomerPaymentAllocationTargetType.Sale => CustomerPaymentObligationTargetType.Sale,
                CustomerPaymentAllocationTargetType.InitialCustomerReceivable =>
                    CustomerPaymentObligationTargetType.InitialCustomerReceivable,
                _ => (CustomerPaymentObligationTargetType?)null
            };
            if (targetType is null)
                return IntegrityFailure();

            var obligationResult = await obligationReadService.GetDetailAsync(
                targetType.Value, allocation.TargetId, cancellationToken);
            if (!obligationResult.Success || obligationResult.Item is null)
                return CustomerPaymentReceiptResult.Failed(Map(obligationResult.FailureReason));

            var obligation = obligationResult.Item;
            var history = obligation.History.SingleOrDefault(item => item.AllocationId == allocation.Id);
            if (history is null
                || history.PaymentId != payment.Id
                || !string.Equals(history.PaymentReference, payment.Reference, StringComparison.Ordinal)
                || history.PaymentDate != payment.PaymentDate
                || history.PaymentMethod != payment.PaymentMethod
                || history.AllocatedAmountMinor != allocation.AllocatedAmountMinor
                || obligation.TargetType != targetType.Value
                || obligation.TargetId != allocation.TargetId
                || !string.Equals(obligation.Reference, allocation.TargetReference, StringComparison.Ordinal)
                || obligation.OriginalAmountMinor != allocation.OriginalObligationAmountMinor)
                return IntegrityFailure();

            if (paymentCreatedAtUtc is DateTimeOffset knownTimestamp
                && knownTimestamp != history.PaymentCreatedAtUtc)
                return IntegrityFailure();
            paymentCreatedAtUtc = history.PaymentCreatedAtUtc;

            receiptAllocations.Add(new CustomerPaymentReceiptAllocationItem(
                allocation.TargetType,
                allocation.TargetReference,
                allocation.OriginalObligationAmountMinor,
                allocation.AllocatedAmountMinor,
                history.CumulativePaidAmountMinor,
                history.RemainingAfterPaymentMinor));
        }

        if (paymentCreatedAtUtc is null)
            return IntegrityFailure();

        return CustomerPaymentReceiptResult.Succeeded(new CustomerPaymentReceiptItem(
            payment.Reference,
            payment.PaymentDate,
            paymentCreatedAtUtc.Value,
            string.IsNullOrWhiteSpace(payment.ClientName) ? "Client anonyme" : payment.ClientName,
            payment.AmountMinor,
            payment.PaymentMethod,
            payment.UnallocatedAmountMinor,
            receiptAllocations));
    }

    private static CustomerPaymentReceiptResult IntegrityFailure() =>
        CustomerPaymentReceiptResult.Failed(CustomerPaymentReceiptFailureReason.DataIntegrityViolation);

    private static CustomerPaymentReceiptFailureReason Map(CustomerPaymentDetailFailureReason reason) => reason switch
    {
        CustomerPaymentDetailFailureReason.NotAuthenticated => CustomerPaymentReceiptFailureReason.NotAuthenticated,
        CustomerPaymentDetailFailureReason.Forbidden => CustomerPaymentReceiptFailureReason.Forbidden,
        CustomerPaymentDetailFailureReason.InvalidPaymentId => CustomerPaymentReceiptFailureReason.InvalidPaymentId,
        CustomerPaymentDetailFailureReason.NotFound => CustomerPaymentReceiptFailureReason.NotFound,
        _ => CustomerPaymentReceiptFailureReason.DataIntegrityViolation
    };

    private static CustomerPaymentReceiptFailureReason Map(CustomerPaymentObligationReadFailureReason reason) =>
        reason switch
        {
            CustomerPaymentObligationReadFailureReason.NotAuthenticated =>
                CustomerPaymentReceiptFailureReason.NotAuthenticated,
            CustomerPaymentObligationReadFailureReason.Forbidden => CustomerPaymentReceiptFailureReason.Forbidden,
            CustomerPaymentObligationReadFailureReason.NotFound => CustomerPaymentReceiptFailureReason.NotFound,
            _ => CustomerPaymentReceiptFailureReason.DataIntegrityViolation
        };
}
