using FinancialManagement.Desktop.Formatting;
using FinancialManagement.Application.Payments;
using FinancialManagement.Domain.Enums;

namespace FinancialManagement.Desktop.ViewModels;

public sealed class PaymentListItemViewModel
{
    internal PaymentListItemViewModel(PaymentObligationListData item)
    {
        TargetKind = item.TargetKind;
        TargetId = item.TargetId;
        PartyId = item.PartyId;
        TargetType = PaymentLabels.FormatTargetType(item.TargetKind);
        Reference = item.Reference;
        BusinessDateValue = item.BusinessDate;
        BusinessDate = item.BusinessDate.ToString("dd/MM/yyyy");
        PartyName = string.IsNullOrWhiteSpace(item.PartyName) ? "Client anonyme" : item.PartyName;
        OriginalAmount = MoneyFormatter.FormatMinor(item.OriginalAmountMinor);
        PaidAmount = MoneyFormatter.FormatMinor(item.PaidAmountMinor);
        RemainingAmount = MoneyFormatter.FormatMinor(item.RemainingAmountMinor);
        OriginalAmountMinor = item.OriginalAmountMinor;
        RemainingAmountMinor = item.RemainingAmountMinor;
        Status = PaymentLabels.FormatStatus(item.Status);
        HasRemaining = item.RemainingAmountMinor > 0 && item.PartyId is > 0;
    }

    internal PaymentTargetKind TargetKind { get; }

    public long TargetId { get; }

    internal long? PartyId { get; }

    public string TargetType { get; }

    public string Reference { get; }

    public string BusinessDate { get; }

    internal DateOnly BusinessDateValue { get; }

    public string PartyName { get; }

    public string OriginalAmount { get; }

    public string PaidAmount { get; }

    public string RemainingAmount { get; }

    internal long OriginalAmountMinor { get; }

    internal long RemainingAmountMinor { get; }

    public string Status { get; }

    public bool HasRemaining { get; }
}

public sealed class PaymentHistoryItemViewModel
{
    internal PaymentHistoryItemViewModel(PaymentHistoryData item)
    {
        PaymentReference = item.PaymentReference;
        PaymentTimestamp = $"{item.PaymentDate:dd/MM/yyyy} {item.PaymentCreatedAtUtc.ToLocalTime():HH:mm}";
        PaymentMethod = PaymentLabels.FormatPaymentMethod(item.PaymentMethod);
        AllocatedAmount = MoneyFormatter.FormatMinor(item.AllocatedAmountMinor);
        CumulativePaidAmount = MoneyFormatter.FormatMinor(item.CumulativePaidAmountMinor);
        RemainingAfterPayment = MoneyFormatter.FormatMinor(item.RemainingAfterPaymentMinor);
    }

    public string PaymentReference { get; }
    public string PaymentTimestamp { get; }
    public string PaymentMethod { get; }
    public string AllocatedAmount { get; }
    public string CumulativePaidAmount { get; }
    public string RemainingAfterPayment { get; }
}

public sealed class AdvancePaymentSourceItemViewModel
{
    internal AdvancePaymentSourceItemViewModel(AdvancePaymentSourceData item)
    {
        Id = item.Id;
        PartyId = item.PartyId;
        Reference = item.Reference;
        PartyName = item.PartyName;
        PaymentDate = item.PaymentDate.ToString("dd/MM/yyyy");
        PaymentMethod = PaymentLabels.FormatPaymentMethod(item.PaymentMethod);
        Amount = MoneyFormatter.FormatMinor(item.AmountMinor);
        AllocatedAmount = MoneyFormatter.FormatMinor(item.AllocatedAmountMinor);
        AvailableAmount = MoneyFormatter.FormatMinor(item.UnallocatedAmountMinor);
        UnallocatedAmountMinor = item.UnallocatedAmountMinor;
    }

    internal long Id { get; }

    internal long PartyId { get; }

    public string Reference { get; }

    public string PartyName { get; }

    public string PaymentDate { get; }

    public string PaymentMethod { get; }

    public string Amount { get; }

    public string AllocatedAmount { get; }

    public string AvailableAmount { get; }

    internal long UnallocatedAmountMinor { get; }
}

public sealed class PaymentAllocationDetailItemViewModel
{
    internal PaymentAllocationDetailItemViewModel(PaymentAllocationDetailData item)
    {
        TargetType = PaymentLabels.FormatTargetType(item.TargetKind);
        TargetReference = item.TargetReference;
        OriginalAmount = MoneyFormatter.FormatMinor(item.OriginalAmountMinor);
        AllocatedAmount = MoneyFormatter.FormatMinor(item.AllocatedAmountMinor);
    }

    public string TargetType { get; }

    public string TargetReference { get; }

    public string OriginalAmount { get; }

    public string AllocatedAmount { get; }
}

public sealed class PaymentObligationItemViewModel : ViewModelBase
{
    private string _allocationAmount = string.Empty;

    internal PaymentObligationItemViewModel(PaymentObligationData item)
    {
        TargetKind = item.TargetKind;
        TargetId = item.TargetId;
        TargetType = PaymentLabels.FormatTargetType(item.TargetKind);
        Reference = item.Reference;
        BusinessDate = item.BusinessDate.ToString("dd/MM/yyyy");
        DueDate = item.DueDate?.ToString("dd/MM/yyyy") ?? "—";
        OriginalAmount = MoneyFormatter.FormatMinor(item.OriginalAmountMinor);
        OutstandingAmount = MoneyFormatter.FormatMinor(item.OutstandingAmountMinor);
        OutstandingAmountMinor = item.OutstandingAmountMinor;
    }

    internal PaymentTargetKind TargetKind { get; }

    internal long TargetId { get; }

    public string TargetType { get; }

    public string Reference { get; }

    public string BusinessDate { get; }

    public string DueDate { get; }

    public string OriginalAmount { get; }

    public string OutstandingAmount { get; }

    internal long OutstandingAmountMinor { get; }

    public string AllocationAmount
    {
        get => _allocationAmount;
        set => SetProperty(ref _allocationAmount, value);
    }

    internal bool TryGetAllocation(out PaymentAllocationRequest? allocation)
    {
        allocation = null;
        if (string.IsNullOrWhiteSpace(AllocationAmount))
        {
            return true;
        }

        if (!MoneyParser.TryParsePositiveMinorUnits(AllocationAmount, out var amountMinor))
        {
            return false;
        }

        allocation = new PaymentAllocationRequest(TargetKind, TargetId, amountMinor);
        return true;
    }
}

public sealed class PaymentPartyLookupItemViewModel
{
    internal PaymentPartyLookupItemViewModel(PartyLookupData item)
    {
        Id = item.Id;
        DisplayName = item.DisplayName;
        DisplayLabel = string.IsNullOrWhiteSpace(item.Identifier)
            ? item.DisplayName
            : $"{item.DisplayName} — {item.Identifier}";
    }

    internal long Id { get; }

    public string DisplayName { get; }

    public string DisplayLabel { get; }
}

public sealed record PaymentMethodFilterOptionViewModel(
    PaymentMethod? Value,
    string DisplayName);

public sealed record PaymentStatusFilterOptionViewModel(
    PaymentObligationStatus? Value,
    string DisplayName);

internal enum PaymentTargetKind
{
    Sale,
    InitialCustomerReceivable,
    Purchase,
    InitialSupplierPayable
}

internal enum PaymentFailureKind
{
    None,
    NotAuthenticated,
    Forbidden,
    Validation,
    NotFound,
    Conflict,
    DataIntegrity,
    Technical
}

internal sealed record PaymentObligationListData(
    PaymentTargetKind TargetKind,
    long TargetId,
    string Reference,
    DateOnly BusinessDate,
    long? PartyId,
    string? PartyName,
    long OriginalAmountMinor,
    long PaidAmountMinor,
    long RemainingAmountMinor,
    PaymentObligationStatus Status);

internal sealed record PaymentPageData(
    bool Success,
    PaymentFailureKind FailureKind,
    string? FailureMessage,
    IReadOnlyList<PaymentObligationListData> Items,
    int PageNumber,
    int PageSize,
    int TotalCount,
    int TotalPages);

internal sealed record PaymentAllocationDetailData(
    PaymentTargetKind TargetKind,
    long TargetId,
    string TargetReference,
    long OriginalAmountMinor,
    long AllocatedAmountMinor);

internal sealed record PaymentDetailData(
    long Id,
    string Reference,
    DateOnly PaymentDate,
    long? PartyId,
    string? PartyName,
    long AmountMinor,
    PaymentMethod PaymentMethod,
    long AllocatedAmountMinor,
    long UnallocatedAmountMinor,
    string? Observation,
    IReadOnlyList<PaymentAllocationDetailData> Allocations);

internal sealed record PaymentDetailResponse(
    bool Success,
    PaymentFailureKind FailureKind,
    string? FailureMessage,
    PaymentDetailData? Item);

internal sealed record AdvancePaymentSourceData(
    long Id,
    string Reference,
    DateOnly PaymentDate,
    long PartyId,
    string PartyName,
    long AmountMinor,
    PaymentMethod PaymentMethod,
    long AllocatedAmountMinor,
    long UnallocatedAmountMinor);

internal sealed record AdvancePaymentSourceResponse(
    bool Success,
    PaymentFailureKind FailureKind,
    string? FailureMessage,
    IReadOnlyList<AdvancePaymentSourceData> Items);

internal sealed record PaymentHistoryData(
    long PaymentId,
    string PaymentReference,
    DateOnly PaymentDate,
    DateTimeOffset PaymentCreatedAtUtc,
    PaymentMethod PaymentMethod,
    long AllocatedAmountMinor,
    long CumulativePaidAmountMinor,
    long RemainingAfterPaymentMinor);

internal sealed record PaymentObligationDetailData(
    PaymentTargetKind TargetKind,
    long TargetId,
    string Reference,
    DateOnly BusinessDate,
    long? PartyId,
    string? PartyName,
    long OriginalAmountMinor,
    long PaidAmountMinor,
    long RemainingAmountMinor,
    PaymentObligationStatus Status,
    IReadOnlyList<PaymentHistoryData> History);

internal sealed record PaymentObligationDetailResponse(
    bool Success,
    PaymentFailureKind FailureKind,
    string? FailureMessage,
    PaymentObligationDetailData? Item);

internal sealed record PartyLookupData(
    long Id,
    string DisplayName,
    string? Identifier);

internal sealed record PartyLookupResponse(
    bool Success,
    PaymentFailureKind FailureKind,
    string? FailureMessage,
    IReadOnlyList<PartyLookupData> Items);

internal sealed record PaymentObligationData(
    PaymentTargetKind TargetKind,
    long TargetId,
    string Reference,
    DateOnly BusinessDate,
    DateOnly? DueDate,
    long OriginalAmountMinor,
    long OutstandingAmountMinor);

internal sealed record PaymentObligationResponse(
    bool Success,
    PaymentFailureKind FailureKind,
    string? FailureMessage,
    IReadOnlyList<PaymentObligationData> Items);

internal sealed record PaymentAllocationRequest(
    PaymentTargetKind TargetKind,
    long TargetId,
    long AmountMinor);

internal sealed record PaymentCreateResponse(
    bool Success,
    PaymentFailureKind FailureKind,
    string? FailureMessage,
    long? PaymentId,
    string? Reference);

internal sealed record AdvanceApplyResponse(
    bool Success,
    PaymentFailureKind FailureKind,
    string? FailureMessage);

internal static class PaymentLabels
{
    public static string FormatPaymentMethod(PaymentMethod paymentMethod) => paymentMethod switch
    {
        PaymentMethod.Cash => "Espèces",
        PaymentMethod.BankTransfer => "Virement bancaire",
        _ => "Mode inconnu"
    };

    public static string FormatTargetType(PaymentTargetKind targetKind) => targetKind switch
    {
        PaymentTargetKind.Sale => "Vente",
        PaymentTargetKind.InitialCustomerReceivable => "Créance initiale",
        PaymentTargetKind.Purchase => "Achat",
        PaymentTargetKind.InitialSupplierPayable => "Dette fournisseur initiale",
        _ => "Obligation"
    };

    public static string FormatStatus(PaymentObligationStatus status) => status switch
    {
        PaymentObligationStatus.Outstanding => "À régler",
        PaymentObligationStatus.PartiallyPaid => "Partiellement réglé",
        PaymentObligationStatus.Settled => "Réglé",
        _ => "Statut inconnu"
    };
}
