using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Application.CustomerPayments;

public sealed record CustomerPaymentListQuery(
    int PageNumber = 1,
    int PageSize = 50,
    DateOnly? FromDate = null,
    DateOnly? ToDate = null,
    string? SearchText = null,
    PaymentMethod? PaymentMethod = null);
