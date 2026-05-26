using RymReportes.Web.Models;
using RymReportes.Web.Options;
using RymReportes.Web.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "RYM Reportes Natura";
});

builder.Services.AddOptions<DatabaseOptions>()
    .Bind(builder.Configuration.GetSection(DatabaseOptions.SectionName))
    .ValidateDataAnnotations();

builder.Services.AddSingleton<ReportPeriodCalculator>();
builder.Services.AddSingleton<OrderNumberNormalizer>();
builder.Services.AddSingleton<IReportExcelGenerator, ClosedXmlReportExcelGenerator>();
builder.Services.AddScoped<IEventReportRepository, SqlEventReportRepository>();
builder.Services.AddScoped<IReportFileService, ReportFileService>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/reportes/mensual/download", async (
    string? month,
    IReportFileService reportFileService,
    CancellationToken cancellationToken) =>
{
    if (!YearMonth.TryParse(month, out var yearMonth))
    {
        return Results.BadRequest(new ProblemDetailsResponse("El parametro month debe tener formato YYYY-MM."));
    }

    var report = await reportFileService.CreateMonthlyReportAsync(yearMonth, cancellationToken);
    return Results.File(
        report.Content,
        report.ContentType,
        report.FileName);
});

app.MapPost("/reportes/pedidos/download", async (
    OrdersReportRequest request,
    IReportFileService reportFileService,
    CancellationToken cancellationToken) =>
{
    if (request.OrderNumbers is null || request.OrderNumbers.Count == 0)
    {
        return Results.BadRequest(new ProblemDetailsResponse("Debe enviar al menos un numero de pedido."));
    }

    try
    {
        var report = await reportFileService.CreateOrdersReportAsync(request.OrderNumbers, cancellationToken);
        return Results.File(
            report.Content,
            report.ContentType,
            report.FileName);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new ProblemDetailsResponse(ex.Message));
    }
});

app.Run();

public sealed record OrdersReportRequest(IReadOnlyList<string> OrderNumbers);

public sealed record ProblemDetailsResponse(string Detail);
