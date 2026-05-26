using System.Globalization;

namespace RymReportes.Web.Models;

public readonly record struct YearMonth(int Year, int Month)
{
    public DateOnly FirstDay => new(Year, Month, 1);

    public static YearMonth FromDate(DateOnly date) => new(date.Year, date.Month);

    public static bool TryParse(string? value, out YearMonth yearMonth)
    {
        yearMonth = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!DateOnly.TryParseExact(
                value.Trim(),
                "yyyy-MM",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed))
        {
            return false;
        }

        yearMonth = new YearMonth(parsed.Year, parsed.Month);
        return true;
    }

    public override string ToString() => FormattableString.Invariant($"{Year:D4}-{Month:D2}");
}
