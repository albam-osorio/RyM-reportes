using System.Text.RegularExpressions;

namespace RymReportes.Web.Services;

public sealed partial class OrderNumberNormalizer
{
    public IReadOnlyList<string> Normalize(IEnumerable<string> values)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values.SelectMany(SplitValue))
        {
            var normalized = value.Trim();
            if (normalized.Length == 0 || !seen.Add(normalized))
            {
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }

    private static string[] SplitValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        return OrderSeparatorsRegex().Split(value);
    }

    [GeneratedRegex(@"[\s,;]+", RegexOptions.Compiled)]
    private static partial Regex OrderSeparatorsRegex();
}
