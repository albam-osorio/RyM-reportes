using RymReportes.Web.Models;

namespace RymReportes.Web.Services;

public sealed class ReportPeriodCalculator
{
    public IReadOnlyList<MonthPeriod> GetAutomaticPeriods(DateOnly executionDate)
    {
        var current = MonthPeriod.FromYearMonth(YearMonth.FromDate(executionDate));
        if (executionDate.Day is < 1 or > 7)
        {
            return [current];
        }

        var previous = MonthPeriod.FromYearMonth(YearMonth.FromDate(executionDate.AddMonths(-1)));
        return [previous, current];
    }

    public MonthPeriod GetMonthPeriod(YearMonth month) => MonthPeriod.FromYearMonth(month);
}
