using ClosedXML.Excel;
using RymReportes.Web.Models;

namespace RymReportes.Web.Services;

public sealed class ClosedXmlReportExcelGenerator : IReportExcelGenerator
{
    private static readonly string[] Headers =
    [
        "nit",
        "remesa",
        "numero_pedido",
        "tipo_evento",
        "fecha_evento",
        "resultado_integracion",
        "respuesta_natura",
        "reportador_por_sisifo"
    ];

    public byte[] Generate(IReadOnlyList<EventReportRow> rows, string title)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Eventos");

        worksheet.Cell(1, 1).Value = title;
        worksheet.Range(1, 1, 1, Headers.Length).Merge();
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 14;

        for (var i = 0; i < Headers.Length; i++)
        {
            var cell = worksheet.Cell(3, i + 1);
            cell.Value = Headers[i];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#1F4E78");
            cell.Style.Font.FontColor = XLColor.White;
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var excelRow = i + 4;
            worksheet.Cell(excelRow, 1).Value = row.Nit;
            worksheet.Cell(excelRow, 2).Value = row.Remesa;
            worksheet.Cell(excelRow, 3).Value = row.NumeroPedido;
            worksheet.Cell(excelRow, 4).Value = row.TipoEvento;
            worksheet.Cell(excelRow, 5).Value = row.FechaEvento;
            worksheet.Cell(excelRow, 6).Value = row.ResultadoIntegracion;
            worksheet.Cell(excelRow, 7).Value = row.RespuestaNatura;
            worksheet.Cell(excelRow, 8).Value = row.ReportadoPorSisifo;
        }

        var lastRow = Math.Max(4, rows.Count + 3);
        worksheet.Range(3, 1, lastRow, Headers.Length).CreateTable("Eventos");
        worksheet.Columns(5, 5).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        worksheet.Columns(8, 8).Style.DateFormat.Format = "yyyy-mm-dd hh:mm:ss";
        worksheet.Columns().AdjustToContents();
        worksheet.SheetView.FreezeRows(3);

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
