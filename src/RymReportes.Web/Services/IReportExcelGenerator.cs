using RymReportes.Web.Models;

namespace RymReportes.Web.Services;

public interface IReportExcelGenerator
{
    byte[] Generate(IReadOnlyList<EventReportRow> rows, string title);
}
