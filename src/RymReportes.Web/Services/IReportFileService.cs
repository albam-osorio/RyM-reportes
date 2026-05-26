using RymReportes.Web.Models;

namespace RymReportes.Web.Services;

public interface IReportFileService
{
    Task<GeneratedReportFile> CreateMonthlyReportAsync(
        YearMonth month,
        CancellationToken cancellationToken);

    Task<GeneratedReportFile> CreateOrdersReportAsync(
        IReadOnlyList<string> orderNumbers,
        CancellationToken cancellationToken);
}
