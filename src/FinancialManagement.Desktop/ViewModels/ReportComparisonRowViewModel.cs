namespace FinancialManagement.Desktop.ViewModels;

public sealed record ReportComparisonRowViewModel(
    string Label,
    string PeriodAValue,
    string PeriodBValue,
    string Variation,
    long? PeriodAResultMinor = null,
    long? PeriodBResultMinor = null)
{
    public bool IsPositivePeriodAResult => PeriodAResultMinor > 0;

    public bool IsNegativePeriodAResult => PeriodAResultMinor < 0;

    public bool IsPositivePeriodBResult => PeriodBResultMinor > 0;

    public bool IsNegativePeriodBResult => PeriodBResultMinor < 0;
}
