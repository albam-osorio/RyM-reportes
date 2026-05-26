namespace RymReportes.Web.Models;

public sealed record MonthPeriod(YearMonth Month, DateOnly StartDate, DateOnly EndDateExclusive)
{
    public static MonthPeriod FromYearMonth(YearMonth month)
    {
        var start = month.FirstDay;
        return new MonthPeriod(month, start, start.AddMonths(1));
    }
}
