using System.Globalization;

namespace FinancialManagement.Desktop.Formatting;

public static class MoneyParser
{
    private static readonly CultureInfo FrenchCulture = CultureInfo.GetCultureInfo("fr-FR");

    public static bool TryParseSignedMinorUnits(string? text, out long amountMinor)
    {
        amountMinor = 0;
        if (string.IsNullOrWhiteSpace(text)
            || !decimal.TryParse(text, NumberStyles.Number, FrenchCulture, out var amount))
        {
            return false;
        }

        decimal scaledAmount;
        try
        {
            scaledAmount = amount * 100m;
        }
        catch (OverflowException)
        {
            return false;
        }

        if (scaledAmount != decimal.Truncate(scaledAmount)
            || scaledAmount < long.MinValue
            || scaledAmount > long.MaxValue)
        {
            return false;
        }

        amountMinor = decimal.ToInt64(scaledAmount);
        return true;
    }

    public static bool TryParsePositiveMinorUnits(string? text, out long amountMinor) =>
        TryParseSignedMinorUnits(text, out amountMinor) && amountMinor > 0;
}
