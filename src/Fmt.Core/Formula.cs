using System.Globalization;

namespace Fmt.Core;

// Arithmetic over {Field} references; an empty reference makes the result empty.
public static class Formula
{
    public static decimal? Evaluate(string? text, Func<string, decimal?> resolve)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        try
        {
            return new Parser(text, resolve).Run();
        }
        catch (FormatException) { return null; }
        catch (OverflowException) { return null; }
    }

    // Returns an error message, or null when valid.
    public static string? Validate(string? text, IEnumerable<string> fieldNames)
    {
        if (string.IsNullOrWhiteSpace(text)) return "Formel ist leer";
        var names = new HashSet<string>(fieldNames, StringComparer.OrdinalIgnoreCase);
        string? unknown = null;
        try
        {
            new Parser(text, n =>
            {
                if (!names.Contains(n)) unknown ??= n;
                return 1m;
            }).Run();
        }
        catch (FormatException e) { return e.Message; }
        catch (OverflowException) { return "Zahl zu groß"; }
        return unknown is null ? null : $"Unbekanntes Feld: {unknown}";
    }

    public static string RenameReference(string formula, string oldName, string newName) =>
        formula.Replace("{" + oldName + "}", "{" + newName + "}", StringComparison.OrdinalIgnoreCase);

    private sealed class Parser(string text, Func<string, decimal?> resolve)
    {
        private int _pos;

        public decimal? Run()
        {
            var value = Sum();
            SkipSpace();
            if (_pos < text.Length) throw new FormatException($"Unerwartetes Zeichen: {text[_pos]}");
            return value;
        }

        private decimal? Sum()
        {
            var left = Product();
            while (Accept('+', '-', out var op))
            {
                var right = Product();
                left = op == '+' ? left + right : left - right;
            }
            return left;
        }

        private decimal? Product()
        {
            var left = Unary();
            while (Accept('*', '/', out var op))
            {
                var right = Unary();
                if (op == '*') left *= right;
                else left = right is null or 0m ? null : left / right;
            }
            return left;
        }

        private decimal? Unary()
        {
            SkipSpace();
            if (_pos < text.Length && text[_pos] == '-')
            {
                _pos++;
                return -Unary();
            }
            return Atom();
        }

        private decimal? Atom()
        {
            SkipSpace();
            if (_pos >= text.Length) throw new FormatException("Formel endet zu früh");
            var c = text[_pos];
            if (c == '(')
            {
                _pos++;
                var inner = Sum();
                SkipSpace();
                if (_pos >= text.Length || text[_pos] != ')') throw new FormatException("Klammer ) fehlt");
                _pos++;
                return inner;
            }
            if (c == '{')
            {
                var end = text.IndexOf('}', _pos);
                if (end < 0) throw new FormatException("Klammer } fehlt");
                var name = text[(_pos + 1)..end].Trim();
                _pos = end + 1;
                return resolve(name);
            }
            var start = _pos;
            while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] is '.' or ',')) _pos++;
            if (start == _pos) throw new FormatException($"Unerwartetes Zeichen: {c}");
            return decimal.Parse(text[start.._pos].Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        }

        private bool Accept(char a, char b, out char op)
        {
            SkipSpace();
            op = _pos < text.Length ? text[_pos] : '\0';
            if (op != a && op != b) return false;
            _pos++;
            return true;
        }

        private void SkipSpace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }
    }
}
