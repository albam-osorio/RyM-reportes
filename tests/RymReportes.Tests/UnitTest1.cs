using RymReportes.Web.Models;
using RymReportes.Web.Services;

namespace RymReportes.Tests;

public class ReportPeriodCalculatorTests
{
    [Fact]
    public void FirstSevenDaysIncludePreviousAndCurrentMonth()
    {
        var calculator = new ReportPeriodCalculator();

        var periods = calculator.GetAutomaticPeriods(new DateOnly(2026, 5, 1));

        Assert.Equal(2, periods.Count);
        Assert.Equal(new YearMonth(2026, 4), periods[0].Month);
        Assert.Equal(new DateOnly(2026, 4, 1), periods[0].StartDate);
        Assert.Equal(new DateOnly(2026, 5, 1), periods[0].EndDateExclusive);
        Assert.Equal(new YearMonth(2026, 5), periods[1].Month);
        Assert.Equal(new DateOnly(2026, 5, 1), periods[1].StartDate);
        Assert.Equal(new DateOnly(2026, 6, 1), periods[1].EndDateExclusive);
    }

    [Fact]
    public void DayEightIncludesOnlyCurrentMonth()
    {
        var calculator = new ReportPeriodCalculator();

        var periods = calculator.GetAutomaticPeriods(new DateOnly(2026, 5, 8));

        var period = Assert.Single(periods);
        Assert.Equal(new YearMonth(2026, 5), period.Month);
        Assert.Equal(new DateOnly(2026, 5, 1), period.StartDate);
        Assert.Equal(new DateOnly(2026, 6, 1), period.EndDateExclusive);
    }
}

public class YearMonthTests
{
    [Fact]
    public void TryParseAcceptsHtmlMonthValue()
    {
        var parsed = YearMonth.TryParse("2026-05", out var month);

        Assert.True(parsed);
        Assert.Equal(new YearMonth(2026, 5), month);
    }
}

public class OrderNumberNormalizerTests
{
    [Fact]
    public void NormalizeTrimsSplitsAndRemovesDuplicates()
    {
        var normalizer = new OrderNumberNormalizer();

        var result = normalizer.Normalize([" 27029697 ", "27029909\n27029697", "", "27036004;27029909"]);

        Assert.Equal(["27029697", "27029909", "27036004"], result);
    }
}

public class ClosedXmlReportExcelGeneratorTests
{
    [Fact]
    public void GenerateCreatesExcelEvenWithoutRows()
    {
        var generator = new ClosedXmlReportExcelGenerator();

        var content = generator.Generate([], "Reporte de eventos 2026-05");

        Assert.True(content.Length > 0);
        Assert.Equal((byte)'P', content[0]);
        Assert.Equal((byte)'K', content[1]);
    }
}

public class ReportFileServiceTests
{
    [Fact]
    public void CreateMonthlyFileNameIncludesGenerationDateAndTime()
    {
        var fileName = ReportFileService.CreateMonthlyFileName(
            new YearMonth(2026, 5),
            new DateTime(2026, 5, 28, 15, 7, 33));

        Assert.Equal("reporte-eventos-2026-05-20260528-1507.xlsx", fileName);
    }
}
