using System.Globalization;
using System.Text.RegularExpressions;

namespace Fmt.Core;

// Understands NosTale notation: 500k, 50kk, 1,5kkk.
public static partial class Gold
{
    [GeneratedRegex(@"^([+-]?)\s*([0-9.,' ]*[0-9])?\s*(k{1,4}|mio|mrd|m|b)?$", RegexOptions.IgnoreCase)]
    private static partial Regex Pattern();

    public static long? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Pattern().Match(text.Trim());
        if (!m.Success || !m.Groups[2].Success) return null;

        var suffix = m.Groups[3].Value.ToLowerInvariant();
        if (!TryNumber(m.Groups[2].Value, suffix.Length > 0, out var value)) return null;

        var factor = suffix switch
        {
            "k" => 1_000m,
            "kk" or "m" or "mio" => 1_000_000m,
            "kkk" or "b" or "mrd" => 1_000_000_000m,
            "kkkk" => 1_000_000_000_000m,
            _ => 1m,
        };
        try
        {
            var result = decimal.Round(value * factor, 0, MidpointRounding.AwayFromZero);
            if (result > long.MaxValue) return null;
            return (long)(m.Groups[1].Value == "-" ? -result : result);
        }
        catch (OverflowException) { return null; }
    }

    // With a suffix the last separator is decimal; without one, a 3-digit group means thousands.
    private static bool TryNumber(string raw, bool hasSuffix, out decimal value)
    {
        var s = raw.Replace(" ", "").Replace("'", "");
        var lastSep = s.LastIndexOfAny(['.', ',']);
        if (lastSep >= 0)
        {
            var digitsAfter = s.Length - lastSep - 1;
            var repeated = s.IndexOf(s[lastSep]) != lastSep;
            var isThousands = repeated || (!hasSuffix && digitsAfter == 3);
            var head = s[..lastSep].Replace(".", "").Replace(",", "");
            s = isThousands ? head + s[(lastSep + 1)..] : head + "." + s[(lastSep + 1)..];
        }
        return decimal.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value);
    }

    // Compact display, e.g. 1,5kk.
    public static string Format(long value)
    {
        var sign = value < 0 ? "-" : "";
        var abs = Math.Abs((decimal)value);
        return abs switch
        {
            >= 1_000_000_000m => sign + Trim(abs / 1_000_000_000m, 2) + "kkk",
            >= 1_000_000m => sign + Trim(abs / 1_000_000m, 2) + "kk",
            >= 1_000m => sign + Trim(abs / 1_000m, 1) + "k",
            _ => sign + abs.ToString("0", CultureInfo.InvariantCulture),
        };
    }

    public static string FormatSigned(long value) => (value > 0 ? "+" : "") + Format(value);

    public static string FormatFull(long value) => value.ToString("#,0", new CultureInfo("de-DE"));

    private static string Trim(decimal v, int decimals) =>
        decimal.Round(v, decimals, MidpointRounding.ToZero).ToString("0." + new string('#', decimals), new CultureInfo("de-DE"));
}

public static partial class Duration
{
    [GeneratedRegex(@"^(?:(\d+)\s*h)?\s*(?:(\d+)\s*m(?:in)?)?$", RegexOptions.IgnoreCase)]
    private static partial Regex HourMinute();

    // Accepts 90, 1:30, 1h 30m, 45m.
    public static int? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Trim();
        if (int.TryParse(s, out var plain)) return plain >= 0 ? plain : null;
        var colon = s.Split(':');
        if (colon.Length == 2 && int.TryParse(colon[0], out var h) && int.TryParse(colon[1], out var mm) && h >= 0 && mm is >= 0 and < 60)
            return h * 60 + mm;
        var match = HourMinute().Match(s);
        if (!match.Success || (!match.Groups[1].Success && !match.Groups[2].Success)) return null;
        var hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
        var minutes = match.Groups[2].Success ? int.Parse(match.Groups[2].Value) : 0;
        return hours * 60 + minutes;
    }

    public static string Format(int minutes)
    {
        if (minutes < 60) return $"{minutes}m";
        return minutes % 60 == 0 ? $"{minutes / 60}h" : $"{minutes / 60}h {minutes % 60}m";
    }
}
