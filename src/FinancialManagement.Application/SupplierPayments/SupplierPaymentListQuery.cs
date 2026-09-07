using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.SupplierPayments;

public sealed record SupplierPaymentListQuery(
    int PageNumber = 1,
    int PageSize = 50,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? SearchText = null,
    PaymentMethod? PaymentMethod = null);
