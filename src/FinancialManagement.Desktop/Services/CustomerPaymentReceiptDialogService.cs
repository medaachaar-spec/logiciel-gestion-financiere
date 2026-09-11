using System.Windows;
using FinancialManagement.Application.CustomerPayments;
using FinancialManagement.Desktop.Views;

namespace FinancialManagement.Desktop.Services;

public interface ICustomerPaymentReceiptDialogService
{
    Task<CustomerPaymentReceiptDialogResult> ShowAsync(
        long customerPaymentId,
        CancellationToken cancellationToken = default);
}

public sealed record CustomerPaymentReceiptDialogResult(bool Success, string? ErrorMessage)
{
    public static CustomerPaymentReceiptDialogResult Succeeded() => new(true, null);
    public static CustomerPaymentReceiptDialogResult Failed(string message) => new(false, message);
}

internal sealed class CustomerPaymentReceiptDialogService(
    ICustomerPaymentReceiptService receiptService,
    ICustomerPaymentReceiptPrinter printer) : ICustomerPaymentReceiptDialogService
{
    public async Task<CustomerPaymentReceiptDialogResult> ShowAsync(
        long customerPaymentId,
        CancellationToken cancellationToken = default)
    {
        var result = await receiptService.GetAsync(customerPaymentId, cancellationToken);
        if (!result.Success || result.Item is null)
            return CustomerPaymentReceiptDialogResult.Failed(MapMessage(result.FailureReason));

        var window = new CustomerPaymentReceiptWindow(result.Item, printer)
        {
            Owner = System.Windows.Application.Current?.Windows
                .OfType<Window>()
                .FirstOrDefault(candidate => candidate.IsActive)
        };
        window.ShowDialog();
        return CustomerPaymentReceiptDialogResult.Succeeded();
    }

    private static string MapMessage(CustomerPaymentReceiptFailureReason reason) => reason switch
    {
        CustomerPaymentReceiptFailureReason.NotAuthenticated => "Votre session n'est plus active.",
        CustomerPaymentReceiptFailureReason.Forbidden =>
            "Vous n'êtes pas autorisé à consulter ce reçu d'encaissement.",
        CustomerPaymentReceiptFailureReason.InvalidPaymentId => "Le paiement sélectionné n'est pas valide.",
        CustomerPaymentReceiptFailureReason.NotFound => "Ce paiement n'existe plus.",
        _ => "Le reçu ne peut pas être généré car les données financières sont incohérentes."
    };
}
