using System.Globalization;

namespace Fmt.Core;

// Real money amounts: 100, 19,99, 100.00€, 1.000,50.
public static class Money
{
    private static readonly CultureInfo German = new("de-DE");

    public static decimal? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var s = text.Replace("€", "").Replace("EUR", "", StringComparison.OrdinalIgnoreCase).Replace(" ", "");
        var dot = s.LastIndexOf('.');
        var comma = s.LastIndexOf(',');
        if (dot >= 0 && comma >= 0)
        {
            // The later separator holds the cents.
            s = dot > comma ? s.Replace(",", "") : s.Replace(".", "").Replace(',', '.');
        }
        else if (comma >= 0)
        {
            s = s.Replace(',', '.');
        }
        else if (dot >= 0 && (s.Length - dot - 1 == 3 || s.IndexOf('.') != dot))
        {
            s = s.Replace(".", "");
        }
        return decimal.TryParse(s, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value)
            ? decimal.Round(value, 2, MidpointRounding.AwayFromZero)
            : null;
    }

    public static string Format(decimal value) => value.ToString("#,##0.00", German) + " €";

    public static string FormatInput(decimal value) => value.ToString("0.00", German);
}
