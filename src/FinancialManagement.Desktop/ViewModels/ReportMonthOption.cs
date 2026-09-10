using System.Globalization;

namespace FinancialManagement.Desktop.ViewModels;

public sealed record ReportMonthOption(DateOnly StartDate)
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    public DateOnly EndDate { get; } = StartDate.AddMonths(1).AddDays(-1);

    public string DisplayName { get; } = FrenchCulture.TextInfo.ToTitleCase(
        StartDate.ToString("MMMM yyyy", FrenchCulture));
}
