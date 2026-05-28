using RymReportes.Web.Models;

namespace RymReportes.Web.Services;

public sealed class ReportFileService(
    OrderNumberNormalizer orderNumberNormalizer,
    IEventReportRepository repository,
    IReportExcelGenerator excelGenerator) : IReportFileService
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public async Task<GeneratedReportFile> CreateMonthlyReportAsync(
        YearMonth month,
        CancellationToken cancellationToken)
    {
        var period = MonthPeriod.FromYearMonth(month);
        var rows = await repository.GetMonthlyReportAsync(period, cancellationToken);
        var content = excelGenerator.Generate(rows, $"Reporte de eventos {month}");

        return new GeneratedReportFile(
            CreateMonthlyFileName(month, DateTime.Now),
            ExcelContentType,
            content,
            rows.Count);
    }

    public async Task<GeneratedReportFile> CreateOrdersReportAsync(
        IReadOnlyList<string> orderNumbers,
        CancellationToken cancellationToken)
    {
        var normalized = orderNumberNormalizer.Normalize(orderNumbers);
        if (normalized.Count == 0)
        {
            throw new InvalidOperationException("Debe enviar al menos un numero de pedido valido.");
        }

        var rows = await repository.GetOrdersReportAsync(normalized, cancellationToken);
        var content = excelGenerator.Generate(rows, "Reporte de eventos por pedidos");

        return new GeneratedReportFile(
            $"reporte-eventos-pedidos-{DateTime.Now:yyyyMMdd-HHmm}.xlsx",
            ExcelContentType,
            content,
            rows.Count);
    }

    public static string CreateMonthlyFileName(YearMonth month, DateTime generatedAt) =>
        $"reporte-eventos-{month}-{generatedAt:yyyyMMdd-HHmm}.xlsx";
}
