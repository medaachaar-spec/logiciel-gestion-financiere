namespace FinancialManagement.Desktop.ViewModels;

public sealed class DashboardMetricViewModel
{
    public DashboardMetricViewModel(string title, string value, string subtitle)
    {
        Title = title;
        Value = value;
        Subtitle = subtitle;
    }

    public string Title { get; }

    public string Value { get; }

    public string Subtitle { get; }
}
