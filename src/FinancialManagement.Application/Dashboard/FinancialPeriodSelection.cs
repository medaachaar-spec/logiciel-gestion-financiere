namespace FinancialManagement.Application.Dashboard;

public sealed record FinancialPeriodSelection(
    DateOnly StartDate,
    DateOnly EndDate,
    FinancialDashboardGranularity Granularity)
{
    public const int MaximumDailyCustomRangeDays = 62;

    public static FinancialPeriodSelection Resolve(
        FinancialPeriodPreset preset,
        DateOnly localToday,
        DateOnly? customStartDate = null,
        DateOnly? customEndDate = null)
    {
        return preset switch
        {
            FinancialPeriodPreset.Today =>
                new(localToday, localToday, FinancialDashboardGranularity.Daily),
            FinancialPeriodPreset.CurrentMonth =>
                ResolveMonth(localToday.Year, localToday.Month),
            FinancialPeriodPreset.CurrentYear =>
                new(
                    new DateOnly(localToday.Year, 1, 1),
                    new DateOnly(localToday.Year, 12, 31),
                    FinancialDashboardGranularity.Monthly),
            FinancialPeriodPreset.Custom => ResolveCustom(customStartDate, customEndDate),
            _ => throw new ArgumentOutOfRangeException(nameof(preset))
        };
    }

    public static FinancialPeriodSelection ResolveMonth(int year, int month)
    {
        var start = new DateOnly(year, month, 1);
        return new(start, start.AddMonths(1).AddDays(-1), FinancialDashboardGranularity.Daily);
    }

    private static FinancialPeriodSelection ResolveCustom(
        DateOnly? customStartDate,
        DateOnly? customEndDate)
    {
        if (customStartDate is not DateOnly start || customEndDate is not DateOnly end)
        {
            throw new ArgumentException("Les dates de début et de fin sont obligatoires.");
        }

        if (start > end)
        {
            throw new ArgumentException("La date de début doit précéder la date de fin.");
        }

        var dayCount = end.DayNumber - start.DayNumber + 1;
        return new(
            start,
            end,
            dayCount <= MaximumDailyCustomRangeDays
                ? FinancialDashboardGranularity.Daily
                : FinancialDashboardGranularity.Monthly);
    }
}
