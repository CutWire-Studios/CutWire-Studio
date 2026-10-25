using System.Globalization;

namespace CutWire.Studio.Application.Stock;

/// <summary>Lenient parser for free-text range filter values such as "10-60", "10..60", "10 to 60", "10-", "-60" or "30".</summary>
public readonly record struct RangeFilter(double? Min, double? Max)
{
    public bool IsEmpty => Min is null && Max is null;

    public static RangeFilter Parse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return default;
        var text = raw.Trim().ToLowerInvariant()
            .Replace("..", "-").Replace("–", "-").Replace("—", "-").Replace(" to ", "-").Replace("_", "-").Replace(",", "-").Replace(":", "-");

        var parts = text.Split('-', StringSplitOptions.None).Select(p => p.Trim()).ToArray();
        if (parts.Length == 1)
            return Number(parts[0]) is { } single ? new RangeFilter(single, single) : default;

        // "10-60", "10-", "-60"; any further dashes (negative numbers) are not supported and ignored.
        var min = Number(parts[0]);
        var max = Number(parts[1]);
        if (min is { } lo && max is { } hi && lo > hi) (min, max) = (hi, lo);
        return new RangeFilter(min, max);
    }

    private static double? Number(string s)
    {
        s = new string(s.TakeWhile(c => char.IsAsciiDigit(c) || c == '.').ToArray());
        return s.Length > 0 && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v >= 0 ? v : null;
    }
}
