using RymReportes.Web.Models;

namespace RymReportes.Web.Services;

public interface IEventReportRepository
{
    Task<IReadOnlyList<EventReportRow>> GetMonthlyReportAsync(
        MonthPeriod period,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EventReportRow>> GetOrdersReportAsync(
        IReadOnlyList<string> orderNumbers,
        CancellationToken cancellationToken);
}
