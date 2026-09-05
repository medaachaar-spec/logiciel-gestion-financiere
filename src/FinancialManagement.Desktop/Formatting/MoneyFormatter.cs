using System.Globalization;

namespace FinancialManagement.Desktop.Formatting;

public static class MoneyFormatter
{
    public static string FormatMinor(long amountMinor) =>
        ((decimal)amountMinor / 100m).ToString("N2", CultureInfo.GetCultureInfo("fr-FR"));
}
