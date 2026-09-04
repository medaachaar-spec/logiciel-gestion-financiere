using System.Globalization;
using System.Security.Cryptography;
using FinancialManagement.Application.Sales;

namespace FinancialManagement.Infrastructure.Sales;

internal sealed class BusinessReferenceGenerator : IBusinessReferenceGenerator
{
    public string GenerateSaleReference(DateOnly businessDate) =>
        Generate("VTE", businessDate);

    public string GenerateCustomerPaymentReference(DateOnly businessDate) =>
        Generate("ENC", businessDate);

    public string GeneratePurchaseReference(DateOnly businessDate) =>
        Generate("ACH", businessDate);

    public string GenerateSupplierPaymentReference(DateOnly businessDate) =>
        Generate("DEC", businessDate);

    private static string Generate(string prefix, DateOnly businessDate) =>
        $"{prefix}-{businessDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}-" +
        RandomNumberGenerator.GetHexString(12);
}
