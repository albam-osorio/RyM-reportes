namespace RymReportes.Web.Models;

public sealed record EventReportRow(
    string? Nit,
    string? Remesa,
    string? NumeroPedido,
    string? TipoEvento,
    DateTime? FechaEvento,
    string? ResultadoIntegracion,
    string? RespuestaNatura,
    DateTime? ReportadoPorSisifo);
