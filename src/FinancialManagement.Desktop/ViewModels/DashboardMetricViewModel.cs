namespace FinancialManagement.Desktop.ViewModels;

public sealed class DashboardMetricViewModel
{
    public DashboardMetricViewModel(
        string title,
        string value,
        string subtitle,
        string iconGlyph = "",
        long? semanticResultMinor = null)
    {
        Title = title;
        Value = value;
        Subtitle = subtitle;
        IconGlyph = iconGlyph;
        IsPositiveResult = semanticResultMinor > 0;
        IsNegativeResult = semanticResultMinor < 0;
    }

    public string Title { get; }

    public string Value { get; }

    public string Subtitle { get; }

    public string IconGlyph { get; }

    public bool IsPositiveResult { get; }

    public bool IsNegativeResult { get; }

}
