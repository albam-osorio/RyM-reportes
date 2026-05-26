namespace RymReportes.Web.Models;

public sealed record GeneratedReportFile(
    string FileName,
    string ContentType,
    byte[] Content,
    int RowCount);
